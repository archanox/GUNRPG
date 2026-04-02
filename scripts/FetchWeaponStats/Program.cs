// Fetches weapon stats from the TrueGameData API and writes a deterministic
// balance snapshot to balances/YYYY-MM-DD.json.
//
// Endpoints:
//   POST https://www.truegamedata.com/api/weapons/api.php?game=bo7&action=calc_damage_table
//   POST https://www.truegamedata.com/api/weapons/api.php?game=bo7&action=calc_stat_summary
//
// Reads weapon names from  data/weapons.json  (relative to the repository root).
// Compares new weapons payload against the most recent file in balances/.
// Writes a new snapshot only when the data changes; exits 0 in both cases.
// Exits non-zero on API or I/O errors so GitHub Actions marks the step failed.

using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

const string ApiBase = "https://www.truegamedata.com/api/weapons/api.php";
const string Game = "bo7";
const int TimeoutSeconds = 30;

// Locate the repository root (two levels above this project's directory).
var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
var weaponsFile = Path.Combine(repoRoot, "data", "weapons.json");
var balancesDir = Path.Combine(repoRoot, "balances");

// ---------------------------------------------------------------------------
// Load weapon list
// ---------------------------------------------------------------------------

List<string> weapons;
try
{
    var weaponsJson = await File.ReadAllTextAsync(weaponsFile);
    weapons = JsonSerializer.Deserialize<List<string>>(weaponsJson)
        ?? throw new InvalidOperationException("Weapon list deserialised to null.");
}
catch (Exception ex)
{
    Console.Error.WriteLine($"ERROR: Could not load {weaponsFile}: {ex.Message}");
    return 1;
}

if (weapons.Count == 0)
{
    Console.Error.WriteLine("ERROR: Weapon list is empty.");
    return 1;
}

var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
Console.WriteLine($"Building balance snapshot for {today} ({weapons.Count} weapon(s))…\n");

// ---------------------------------------------------------------------------
// HTTP client with required headers
// ---------------------------------------------------------------------------

using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(TimeoutSeconds) };
httpClient.DefaultRequestHeaders.Add("Accept", "application/json, text/plain, */*");
httpClient.DefaultRequestHeaders.Add("Origin", "capacitor://com.truegamedata.app");
httpClient.DefaultRequestHeaders.Add("User-Agent",
    "Mozilla/5.0 (iPhone; CPU iPhone OS 18_7 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Mobile/15E148");
httpClient.DefaultRequestHeaders.Add("Sec-Fetch-Site", "cross-site");
httpClient.DefaultRequestHeaders.Add("Sec-Fetch-Mode", "cors");
httpClient.DefaultRequestHeaders.Add("Sec-Fetch-Dest", "empty");

// ---------------------------------------------------------------------------
// Per-weapon fetch
// ---------------------------------------------------------------------------

var weaponsData = new SortedDictionary<string, JsonObject>(StringComparer.Ordinal);

foreach (var weapon in weapons)
{
    Console.WriteLine($"  Fetching damage table for '{weapon}'…");
    var damageTable = await PostApiAsync(weapon, "calc_damage_table");
    if (damageTable is null) return 1;

    Console.WriteLine($"  Fetching stat summary for '{weapon}'…");
    var summary = await PostApiAsync(weapon, "calc_stat_summary");
    if (summary is null) return 1;

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

var snapshot = new JsonObject
{
    ["version"] = today,
    ["weapons"] = weaponsNode
};

// ---------------------------------------------------------------------------
// Change detection
// ---------------------------------------------------------------------------

Directory.CreateDirectory(balancesDir);
var latestSnapshot = LoadLatestSnapshot(balancesDir);

if (latestSnapshot is not null)
{
    var newWeaponsNorm = Normalise(snapshot["weapons"]);
    var oldWeaponsNorm = Normalise(latestSnapshot["weapons"]);
    if (newWeaponsNorm == oldWeaponsNorm)
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
return 0;

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

async Task<JsonObject?> PostApiAsync(string weapon, string action)
{
    var url = $"{ApiBase}?game={Game}&action={action}";
    var body = JsonSerializer.Serialize(new { weapon, attachments = Array.Empty<object>(), health = "100" });
    using var content = new StringContent(body, Encoding.UTF8, "application/json");

    HttpResponseMessage response;
    try
    {
        response = await httpClient.PostAsync(url, content);
        response.EnsureSuccessStatusCode();
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"ERROR: API unreachable for weapon={weapon} action={action}: {ex.Message}");
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
        Console.Error.WriteLine($"ERROR: Could not parse API response for weapon={weapon} action={action}: {ex.Message}");
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
            Console.Error.WriteLine($"ERROR: Could not parse inner JSON for weapon={weapon} action={action}: {ex.Message}");
            return null;
        }
    }

    if (node is not JsonObject obj)
    {
        Console.Error.WriteLine($"ERROR: Unexpected top-level JSON type for weapon={weapon} action={action}.");
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

    foreach (var node in entries)
    {
        var entry = node?.AsObject();
        if (entry is null) continue;

        var range = new JsonObject
        {
            ["range_m"]   = Fixed(GetDouble(entry, "dropoff")),
            ["head"]      = Fixed(GetDouble(entry, "head")),
            ["neck"]      = Fixed(GetDouble(entry, "neck")),
            ["chest"]     = Fixed(GetDouble(entry, "chest")),
            ["stomach"]   = Fixed(GetDouble(entry, "stomach")),
            ["upper_arm"] = Fixed(GetDouble(entry, "upperarm")),
            ["lower_arm"] = Fixed(GetDouble(entry, "lowerarm")),
            ["upper_leg"] = Fixed(GetDouble(entry, "upperleg")),
            ["lower_leg"] = Fixed(GetDouble(entry, "lowerleg")),
        };
        result.Add(range);
    }

    return result;
}

void MergeSummaryStats(JsonObject stats, JsonObject summary)
{
    TryPick(stats, summary, "mag_size",         ["mag_size", "magazine", "ammo_capacity", "mag"],                  v => (JsonNode)JsonValue.Create((int)Math.Round(v)));
    TryPick(stats, summary, "reload_ms",        ["reload_add_time", "reload_time", "reload_ms", "reload_empty", "reload"], v => (JsonNode)Fixed(v, 0));
    TryPick(stats, summary, "ads_ms",           ["ads_time", "ads_ms", "aim_down_sight", "ads"],                   v => (JsonNode)Fixed(v, 0));
    TryPick(stats, summary, "sprint_to_fire_ms",["sprint_out_time", "sprint_to_fire", "stf_time", "sprint_fire"],  v => (JsonNode)Fixed(v, 0));
    TryPick(stats, summary, "bullet_velocity",  ["bullet_velocity", "muzzle_velocity", "velocity"],                v => (JsonNode)Fixed(v, 1));
    TryPick(stats, summary, "move_speed",       ["movement_speed", "move_speed", "ms", "walk_speed"],              v => (JsonNode)Fixed(v, 4));
    TryPick(stats, summary, "ads_move_speed",   ["ads_movement_speed", "ads_move_speed", "ads_ms_move"],           v => (JsonNode)Fixed(v, 4));
}

void TryPick(JsonObject target, JsonObject source, string outKey, string[] candidates, Func<double, JsonNode> convert)
{
    foreach (var key in candidates)
    {
        if (!source.TryGetPropertyValue(key, out var node) || node is null) continue;
        try { target[outKey] = convert(node.GetValue<double>()); }
        catch { /* skip non-numeric values */ }
        return;
    }
}

// Returns the first list of damage entries from the "damage" outer array.
JsonArray? FirstDamageEntries(JsonObject damageTable)
{
    if (!damageTable.TryGetPropertyValue("damage", out var outer) || outer is not JsonArray outerArr)
        return null;
    return outerArr.Count > 0 ? outerArr[0]?.AsArray() : null;
}

double GetDouble(JsonObject obj, string key) =>
    obj.TryGetPropertyValue(key, out var n) && n is not null ? n.GetValue<double>() : 0.0;

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
