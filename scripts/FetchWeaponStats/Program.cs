// Fetches weapon stats from the TrueGameData API and writes a deterministic
// balance snapshot to balances/YYYY-MM-DD.json.
//
// Endpoints:
//   GET  https://www.truegamedata.com/api/weapons/api.php?game=bo7&action=all_base_data
//   POST https://www.truegamedata.com/api/weapons/api.php?game=bo7&action=calc_damage_table
//   POST https://www.truegamedata.com/api/weapons/api.php?game=bo7&action=calc_stat_summary
//
// Discovers weapons automatically from the API at runtime; falls back to
// data/weapons.json if discovery fails.  Weapons are processed in
// StringComparer.Ordinal order so snapshots are always bit-identical.
// Compares new content (weapons + attachments, excluding version/hash) against
// the most recent file in balances/; skips writing if identical.
// Writes a new snapshot only when the data changes; exits 0 in both cases.
// Exits 0 when the API is unavailable (no new snapshot is written, nothing to commit).
// Exits non-zero only on local failures such as I/O errors or invalid/missing local data.

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Globalization;

const string ApiBase = "https://www.truegamedata.com/api/weapons/api.php";
const string Game = "bo7";
const int TimeoutSeconds = 30;
const int MaxRetryAttempts = 3;
const int RetryDelayMs = 2000;
const string ApiUnavailableWarning = "WARNING: API unavailable — skipping snapshot. Nothing to commit.";
var damageTableRegex = new Regex(@"\[[\s\S]*?\]", RegexOptions.Compiled);

// Locate the repository root from the working directory; works reliably both
// locally (run from the repo root) and in GitHub Actions (checkout sets CWD).
var repoRoot = Directory.GetCurrentDirectory();
var weaponsFile = Path.Combine(repoRoot, "data", "weapons.json");
var balancesDir = Path.Combine(repoRoot, "balances");

// ---------------------------------------------------------------------------
// HTTP client with required headers
// ---------------------------------------------------------------------------

using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(TimeoutSeconds) };
httpClient.DefaultRequestHeaders.Add("Accept", "application/json, text/plain, */*");
httpClient.DefaultRequestHeaders.Add("Accept-Language", "en-AU,en;q=0.9");
httpClient.DefaultRequestHeaders.Add("Origin", "capacitor://com.truegamedata.app");
httpClient.DefaultRequestHeaders.Add("User-Agent",
    "Mozilla/5.0 (iPhone; CPU iPhone OS 18_7 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Mobile/15E148");
httpClient.DefaultRequestHeaders.Add("Sec-Fetch-Site", "cross-site");
httpClient.DefaultRequestHeaders.Add("Sec-Fetch-Mode", "cors");
httpClient.DefaultRequestHeaders.Add("Sec-Fetch-Dest", "empty");
httpClient.DefaultRequestHeaders.Add("Pragma", "no-cache");
httpClient.DefaultRequestHeaders.Add("Cache-Control", "no-cache");

// ---------------------------------------------------------------------------
// Discover weapon list
// ---------------------------------------------------------------------------

var discoveredWeaponBaseData = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
var discoveredWeapons = await DiscoverWeaponsAsync();
if (discoveredWeapons is null)
{
    // Fall back to the manually maintained list when discovery fails.
    Console.WriteLine($"WARNING: API discovery failed — falling back to {weaponsFile}.");
    try
    {
        var weaponsJson = await File.ReadAllTextAsync(weaponsFile);
        discoveredWeapons = JsonSerializer.Deserialize<List<string>>(weaponsJson)
            ?? throw new InvalidOperationException("Weapon list deserialised to null.");
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"ERROR: Could not load fallback weapon list {weaponsFile}: {ex.Message}");
        return 1;
    }
}

if (discoveredWeapons.Count == 0)
{
    Console.Error.WriteLine("ERROR: Weapon list is empty.");
    return 1;
}

// Always process in deterministic order.
var weapons = discoveredWeapons.OrderBy(w => w, StringComparer.Ordinal).ToList();

var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
Console.WriteLine($"Building balance snapshot for {today} ({weapons.Count} weapon(s))…\n");

// ---------------------------------------------------------------------------
// Per-weapon fetch
// ---------------------------------------------------------------------------

var weaponsData = new SortedDictionary<string, JsonObject>(StringComparer.Ordinal);

foreach (var weapon in weapons)
{
    var hasBaseDataFallback = discoveredWeaponBaseData.TryGetValue(weapon, out var baseWeaponData);

    Console.WriteLine($"  Fetching damage table for '{weapon}'…");
    var damageTable = await PostApiAsync(weapon, "calc_damage_table");
    if (damageTable is null)
    {
        if (hasBaseDataFallback)
        {
            Console.WriteLine($"  Falling back to all_base_data for '{weapon}'…");
            weaponsData[weapon] = BuildWeaponStatsFromBaseData(baseWeaponData!);
            Console.WriteLine();
            continue;
        }

        Console.WriteLine(ApiUnavailableWarning);
        return 0;
    }

    Console.WriteLine($"  Fetching stat summary for '{weapon}'…");
    var summary = await PostApiAsync(weapon, "calc_stat_summary");
    if (summary is null)
    {
        if (hasBaseDataFallback)
        {
            Console.WriteLine($"  Falling back to all_base_data for '{weapon}'…");
            weaponsData[weapon] = BuildWeaponStatsFromBaseData(baseWeaponData!);
            Console.WriteLine();
            continue;
        }

        Console.WriteLine(ApiUnavailableWarning);
        return 0;
    }

    var stats = BuildWeaponStats(damageTable, summary);
    weaponsData[weapon] = stats;
    Console.WriteLine();
}

// ---------------------------------------------------------------------------
// Build snapshot
// ---------------------------------------------------------------------------

var weaponsNode = new JsonObject();
foreach (var (name, stats) in weaponsData)
    weaponsNode[name] = stats;

// Attachment data is not yet fetched; the empty object reserves the field in
// the schema so no future migration is needed.
var attachmentsNode = new JsonObject();

// Compute a deterministic SHA-256 hash of the normalised content (weapons +
// attachments) so replay validators can confirm they are using identical data.
// The hash covers version + weapons + attachments but NOT the hash field itself.
// Keys are alphabetically ordered here so that Normalise() produces a
// canonical string that is stable regardless of insertion order.
var payloadForHash = new JsonObject
{
    ["attachments"] = attachmentsNode.DeepClone(),
    ["version"]     = today,
    ["weapons"]     = weaponsNode.DeepClone()
};
var normalizedPayload = Normalise(payloadForHash);
var snapshotHash = ComputeSha256Hex(normalizedPayload);

var snapshot = new JsonObject
{
    ["version"]     = today,
    ["hash"]        = snapshotHash,
    ["weapons"]     = weaponsNode,
    ["attachments"] = attachmentsNode
};

// ---------------------------------------------------------------------------
// Change detection (excludes version and hash so date-only changes are ignored)
// ---------------------------------------------------------------------------

Directory.CreateDirectory(balancesDir);
var latestSnapshot = LoadLatestSnapshot(balancesDir);

if (latestSnapshot is not null)
{
    var newContentNorm  = Normalise(new JsonObject
    {
        ["attachments"] = snapshot["attachments"]?.DeepClone(),
        ["weapons"]     = snapshot["weapons"]?.DeepClone()
    });
    var oldContentNorm = Normalise(new JsonObject
    {
        ["attachments"] = latestSnapshot["attachments"]?.DeepClone(),
        ["weapons"]     = latestSnapshot["weapons"]?.DeepClone()
    });
    if (newContentNorm == oldContentNorm)
    {
        Console.WriteLine("No changes detected — snapshot matches the most recent file. Nothing to commit.");
        return 0;
    }
}

// ---------------------------------------------------------------------------
// Write snapshot
// ---------------------------------------------------------------------------

var outPath = Path.Combine(balancesDir, $"{today}.json");
var writeOptions = new JsonSerializerOptions { WriteIndented = true };
var jsonBytes = JsonSerializer.SerializeToUtf8Bytes(snapshot, writeOptions);
await File.WriteAllBytesAsync(outPath, jsonBytes);
// Ensure trailing newline.
await File.AppendAllTextAsync(outPath, "\n", Encoding.UTF8);

Console.WriteLine($"New snapshot written: {outPath}");
Console.WriteLine($"Balance hash: {snapshotHash}");
return 0;

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

// Discovers all weapon identifiers from the API. Returns null when the
// discovery endpoint is unavailable so the caller can fall back to the file.
async Task<List<string>?> DiscoverWeaponsAsync()
{
    Console.WriteLine("Discovering weapons from API…");
    var node = await GetApiAsync("all_base_data");
    if (node is null) return null;

    var discovered = new HashSet<string>(StringComparer.Ordinal);

    // The response may be an array of plain strings or an array of objects
    // with a name/id field, depending on API version.
    if (node is JsonArray arr)
    {
        foreach (var item in arr)
        {
            if (item is JsonValue sv && sv.TryGetValue<string>(out var name) && !string.IsNullOrWhiteSpace(name))
            {
                discovered.Add(name);
            }
            else if (item is JsonObject obj)
            {
                var weaponName = ExtractStringField(obj, ["gun", "name", "weapon_name", "id", "weapon_id"]);
                if (!string.IsNullOrWhiteSpace(weaponName))
                {
                    discovered.Add(weaponName);
                    discoveredWeaponBaseData[weaponName] = obj;
                }
            }
        }
    }
    else if (node is JsonObject topObj)
    {
        // Some endpoints wrap the list in a keyed object, e.g. {"weapons": [...]}
        foreach (var key in new[] { "weapons", "data", "items" })
        {
            if (topObj.TryGetPropertyValue(key, out var inner) && inner is JsonArray innerArr)
            {
                foreach (var item in innerArr)
                {
                    if (item is JsonValue sv && sv.TryGetValue<string>(out var name) && !string.IsNullOrWhiteSpace(name))
                        discovered.Add(name);
                    else if (item is JsonObject obj)
                    {
                        var weaponName = ExtractStringField(obj, ["gun", "name", "weapon_name", "id", "weapon_id"]);
                        if (!string.IsNullOrWhiteSpace(weaponName))
                        {
                            discovered.Add(weaponName);
                            discoveredWeaponBaseData[weaponName] = obj;
                        }
                    }
                }
                break;
            }
        }
    }

    if (discovered.Count == 0)
    {
        Console.Error.WriteLine("WARNING: Weapon discovery endpoint returned no usable weapon names.");
        return null;
    }

    Console.WriteLine($"  Discovered {discovered.Count} weapon(s) from API.\n");
    return discovered.ToList();
}

string? ExtractStringField(JsonObject obj, string[] candidates)
{
    foreach (var key in candidates)
    {
        if (obj.TryGetPropertyValue(key, out var n) && n is JsonValue sv && sv.TryGetValue<string>(out var s))
            return s;
    }
    return null;
}

// GET request with retry, returns the parsed (and unwrapped) JsonNode or null.
async Task<JsonNode?> GetApiAsync(string action)
{
    var url = $"{ApiBase}?game={Game}&action={action}";

    HttpResponseMessage? response = null;
    for (int attempt = 1; attempt <= MaxRetryAttempts; attempt++)
    {
        try
        {
            response = await httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();
            break;
        }
        catch (Exception ex) when (attempt < MaxRetryAttempts)
        {
            Console.Error.WriteLine($"WARNING: GET attempt {attempt} failed for action={action}: {ex.Message} — retrying in 2s…");
            response = null;
            await Task.Delay(RetryDelayMs);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"WARNING: GET failed for action={action}: {ex.Message}");
            return null;
        }
    }

    if (response is null)
    {
        Console.Error.WriteLine($"WARNING: All GET retry attempts failed for action={action}.");
        return null;
    }

    var raw = await response.Content.ReadAsStringAsync();
    JsonNode? node;
    try { node = JsonNode.Parse(raw); }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"WARNING: Could not parse GET response for action={action}: {ex.Message}");
        return null;
    }

    // Unwrap double-encoded responses.
    if (node is JsonValue sv && sv.TryGetValue<string>(out var inner))
    {
        try { node = JsonNode.Parse(inner); }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"WARNING: Could not parse unwrapped GET response for action={action}: {ex.Message}");
            return null;
        }
    }

    return node;
}

string ComputeSha256Hex(string input)
{
    var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
    return Convert.ToHexString(bytes).ToLowerInvariant();
}

async Task<JsonObject?> PostApiAsync(string weapon, string action)
{
    var url = $"{ApiBase}?game={Game}&action={action}";
    var body = action switch
    {
        "calc_stat_summary" => JsonSerializer.Serialize(new
        {
            chartData = new[]
            {
                new
                {
                    weapon,
                    attachments = Array.Empty<object>()
                }
            }
        }),
        _ => JsonSerializer.Serialize(new { weapon, attachments = Array.Empty<object>(), health = "100" })
    };

    HttpResponseMessage? response = null;
    for (int attempt = 1; attempt <= MaxRetryAttempts; attempt++)
    {
        try
        {
            // Create a fresh StringContent each attempt; it is disposed inside
            // the try block after PostAsync has consumed the request body.
            using var content = new StringContent(body, Encoding.UTF8, "application/json");
            response = await httpClient.PostAsync(url, content);
            response.EnsureSuccessStatusCode();
            break;
        }
        catch (Exception ex) when (attempt < MaxRetryAttempts)
        {
            Console.Error.WriteLine($"WARNING: attempt {attempt} failed for weapon={weapon} action={action}: {ex.Message} — retrying in 2s…");
            response = null;
            await Task.Delay(RetryDelayMs);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"WARNING: API unreachable for weapon={weapon} action={action}: {ex.Message}");
            return null;
        }
    }

    if (response is null)
    {
        Console.Error.WriteLine($"WARNING: All retry attempts failed for weapon={weapon} action={action}.");
        return null;
    }

    var raw = await response.Content.ReadAsStringAsync();

    // The API sometimes returns a double-encoded JSON string; unwrap if needed.
    JsonNode? node;
    try
    {
        node = JsonNode.Parse(raw);
    }
    catch (JsonException ex)
    {
        Console.Error.WriteLine($"WARNING: Could not parse API response for weapon={weapon} action={action}: {ex.Message}");
        return null;
    }

    if (node is JsonValue strVal && strVal.TryGetValue<string>(out var inner))
    {
        try
        {
            node = JsonNode.Parse(inner);
        }
        catch (JsonException ex)
        {
            Console.Error.WriteLine($"WARNING: Could not parse inner JSON for weapon={weapon} action={action}: {ex.Message}");
            return null;
        }
    }

    if (node is JsonArray arr)
    {
        var firstObject = arr.Count > 0 && arr[0] is JsonObject first ? first : null;
        if (firstObject is not null)
            return firstObject;

        Console.Error.WriteLine($"WARNING: Unexpected empty array response for weapon={weapon} action={action}.");
        return null;
    }

    if (node is not JsonObject obj)
    {
        Console.Error.WriteLine($"WARNING: Unexpected top-level JSON type for weapon={weapon} action={action}.");
        return null;
    }

    return obj;
}

JsonObject BuildWeaponStats(JsonObject damageTable, JsonObject summary)
{
    var stats = new JsonObject();

    // RPM — reliably present in the damage table.
    var rpm = ExtractRpm(damageTable);
    if (rpm > 0) stats["rpm"] = rpm;

    // Summary stats (mag, reload, ADS, bullet velocity, movement).
    MergeSummaryStats(stats, summary);

    // Damage ranges (per-dropoff-distance per-body-part).
    stats["damage_ranges"] = ExtractDamageRanges(damageTable);

    return stats;
}

JsonObject BuildWeaponStatsFromBaseData(JsonObject baseData)
{
    var stats = new JsonObject();

    var rpm = GetDouble(baseData, "rpm");
    if (rpm > 0) stats["rpm"] = (int)Math.Round(rpm);

    MergeSummaryStats(stats, baseData);

    var damageTables = ParseDamageTables(baseData);
    if (damageTables.Count > 0)
        stats["damage_ranges"] = ExtractDamageRanges(new JsonObject { ["damage"] = damageTables });
    else
        stats["damage_ranges"] = new JsonArray();

    return stats;
}

int ExtractRpm(JsonObject damageTable)
{
    var entries = FirstDamageEntries(damageTable);
    if (entries is null || entries.Count == 0) return 0;
    var first = entries[0]?.AsObject();
    if (first is null) return 0;
    return (int)(first["final_rpm"] ?? first["base_rpm"] ?? JsonValue.Create(0))!.GetValue<double>();
}

JsonArray ExtractDamageRanges(JsonObject damageTable)
{
    var result = new JsonArray();
    var entries = FirstDamageEntries(damageTable);
    if (entries is null) return result;

    // Sort by dropoff distance to ensure deterministic ordering regardless
    // of the order in which the API returns the entries.
    var sortedEntries = entries
        .Select(n => n?.AsObject())
        .Where(n => n is not null)
        .OrderBy(n => GetDouble(n!, "dropoff"))
        .ToList();

    foreach (var entry in sortedEntries)
    {
        var range = new JsonObject
        {
            ["range_m"]   = Fixed(GetDouble(entry!, "dropoff")),
            ["head"]      = Fixed(GetDouble(entry!, "head")),
            ["neck"]      = Fixed(GetDouble(entry!, "neck")),
            ["chest"]     = Fixed(GetDouble(entry!, "chest")),
            ["stomach"]   = Fixed(GetDouble(entry!, "stomach")),
            ["upper_arm"] = Fixed(GetDouble(entry!, "upperarm")),
            ["lower_arm"] = Fixed(GetDouble(entry!, "lowerarm")),
            ["upper_leg"] = Fixed(GetDouble(entry!, "upperleg")),
            ["lower_leg"] = Fixed(GetDouble(entry!, "lowerleg")),
        };
        result.Add(range);
    }

    return result;
}

void MergeSummaryStats(JsonObject stats, JsonObject summary)
{
    TryPick(stats, summary, "mag_size",         ["mag_size", "magazine", "ammo_capacity", "mag"],                                 (v, _) => (JsonNode)JsonValue.Create((int)Math.Round(v)));
    TryPick(stats, summary, "reload_ms",        ["reload_add_time", "reload_time", "reload_ms", "reload_empty", "reload"],       (v, unit) => (JsonNode)Fixed(ConvertToMillisecondsIfNeeded(v, unit), 0));
    TryPick(stats, summary, "ads_ms",           ["ads_time", "ads_ms", "aim_down_sight", "ads"],                                  (v, unit) => (JsonNode)Fixed(ConvertToMillisecondsIfNeeded(v, unit), 0));
    TryPick(stats, summary, "sprint_to_fire_ms",["sprint_out_time", "sprint_to_fire", "stf_time", "sprint_fire", "stf"],         (v, unit) => (JsonNode)Fixed(ConvertToMillisecondsIfNeeded(v, unit), 0));
    TryPick(stats, summary, "bullet_velocity",  ["bullet_velocity", "muzzle_velocity", "velocity", "bv"],                         (v, _) => (JsonNode)Fixed(v, 1));
    TryPick(stats, summary, "move_speed",       ["movement_speed", "move_speed", "ms", "walk_speed", "movement"],                 (v, _) => (JsonNode)Fixed(v, 4));
    TryPick(stats, summary, "ads_move_speed",   ["ads_movement_speed", "ads_move_speed", "ads_ms_move", "ads_movement"],         (v, _) => (JsonNode)Fixed(v, 4));
}

double ConvertToMillisecondsIfNeeded(double value, string? unit) =>
    string.Equals(unit, "s", StringComparison.OrdinalIgnoreCase)
    // all_base_data omits units for timing fields; values under 10 are seconds.
    || (string.IsNullOrWhiteSpace(unit) && value > 0 && value < 10)
        ? value * 1000.0
        : value;

void TryPick(JsonObject target, JsonObject source, string outKey, string[] candidates, Func<double, string?, JsonNode> convert)
{
    foreach (var key in candidates)
    {
        if (!source.TryGetPropertyValue(key, out var node) || node is null) continue;
        if (!TryReadMetricValue(node, out var value, out var unit)) continue;
        target[outKey] = convert(value, unit);
        return;
    }
}

bool TryReadMetricValue(JsonNode node, out double value, out string? unit)
{
    value = 0;
    unit = null;

    if (node is JsonValue valueNode)
    {
        try
        {
            value = valueNode.GetValue<double>();
            return true;
        }
        catch
        {
            return false;
        }
    }

    if (node is not JsonObject obj)
        return false;

    unit = obj["unit"]?.GetValue<string>();

    foreach (var key in new[] { "attachmentValue", "baseValue", "value" })
    {
        if (obj[key] is not JsonValue metricValue)
            continue;

        try
        {
            value = metricValue.GetValue<double>();
            return true;
        }
        catch
        {
            // ignore non-numeric metric values and try the next candidate
        }
    }

    return false;
}

JsonArray ParseDamageTables(JsonObject baseData)
{
    var tables = new JsonArray();
    if (!baseData.TryGetPropertyValue("simple_damage_mp", out var node) || node is not JsonValue rawValue)
        return tables;

    string? rawDamage = null;
    try
    {
        rawDamage = rawValue.GetValue<string>();
    }
    catch
    {
        return tables;
    }

    if (string.IsNullOrWhiteSpace(rawDamage))
        return tables;

    foreach (Match match in damageTableRegex.Matches(rawDamage))
    {
        try
        {
            if (JsonNode.Parse(match.Value) is JsonArray table)
                tables.Add(table);
        }
        catch
        {
            // ignore malformed fallback tables and continue
        }
    }

    return tables;
}

// Returns the first list of damage entries from the "damage" outer array.
JsonArray? FirstDamageEntries(JsonObject damageTable)
{
    if (!damageTable.TryGetPropertyValue("damage", out var outer) || outer is not JsonArray outerArr)
        return null;
    return outerArr.Count > 0 ? outerArr[0]?.AsArray() : null;
}

double GetDouble(JsonObject obj, string key)
{
    if (!obj.TryGetPropertyValue(key, out var node) || node is null)
        return 0.0;

    if (node is JsonValue value)
    {
        try
        {
            return value.GetValue<double>();
        }
        catch
        {
            try
            {
                var stringValue = value.GetValue<string>();
                return double.TryParse(stringValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
                    ? parsed
                    : 0.0;
            }
            catch
            {
                return 0.0;
            }
        }
    }

    return 0.0;
}

// Round to `decimals` places; return an integer node when the result is whole.
JsonNode Fixed(double value, int decimals = 2)
{
    var rounded = Math.Round(value, decimals);
    return rounded == Math.Floor(rounded)
        ? JsonValue.Create((long)rounded)
        : JsonValue.Create(rounded);
}

// Serialise `node` with sorted keys for deterministic comparison.
string Normalise(JsonNode? node)
{
    var opts = new JsonSerializerOptions { WriteIndented = false };
    return SortNode(node).ToJsonString(opts);
}

JsonNode SortNode(JsonNode? node) => node switch
{
    JsonObject obj  => SortObject(obj),
    JsonArray  arr  => SortArray(arr),
    _               => node?.DeepClone() ?? JsonValue.Create<object?>(null)!
};

JsonObject SortObject(JsonObject obj)
{
    var sorted = new JsonObject();
    foreach (var key in obj.Select(kv => kv.Key).OrderBy(k => k, StringComparer.Ordinal))
        sorted[key] = SortNode(obj[key]);
    return sorted;
}

JsonArray SortArray(JsonArray arr)
{
    var sorted = new JsonArray();
    foreach (var item in arr)
        sorted.Add(SortNode(item));
    return sorted;
}

JsonObject? LoadLatestSnapshot(string dir)
{
    var files = Directory.GetFiles(dir, "????-??-??.json")
        .OrderBy(f => f, StringComparer.Ordinal)
        .ToArray();
    if (files.Length == 0) return null;
    var json = File.ReadAllText(files[^1]);
    return JsonNode.Parse(json)?.AsObject();
}
