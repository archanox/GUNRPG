using System.Text.Json;
using System.Text.Json.Serialization;

namespace GUNRPG.Core.Weapons;

public sealed class BalanceSnapshot
{
    public string Version { get; init; } = string.Empty;
    public string Hash { get; init; } = string.Empty;
    public Dictionary<string, BalanceWeaponSnapshot> Weapons { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, BalanceAttachmentSnapshot> Attachments { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class BalanceWeaponSnapshot
{
    [JsonPropertyName("rpm")]
    public int RoundsPerMinute { get; init; }

    [JsonPropertyName("mag_size")]
    public int MagazineSize { get; init; }

    [JsonPropertyName("reload_ms")]
    public int ReloadTimeMs { get; init; }

    [JsonPropertyName("ads_ms")]
    public int ADSTimeMs { get; init; }

    [JsonPropertyName("sprint_to_fire_ms")]
    public float SprintToFireTimeMs { get; init; }

    [JsonPropertyName("bullet_velocity")]
    public float BulletVelocityMetersPerSecond { get; init; }

    [JsonPropertyName("move_speed")]
    public float MovementSpeedMetersPerSecond { get; init; }

    [JsonPropertyName("ads_move_speed")]
    public float ADSMovementSpeedMetersPerSecond { get; init; }

    [JsonPropertyName("damage_ranges")]
    public List<BalanceDamageRangeSnapshot> DamageRanges { get; init; } = new();
}

public sealed class BalanceDamageRangeSnapshot
{
    [JsonPropertyName("range_m")]
    public float RangeMeters { get; init; }

    [JsonPropertyName("head")]
    public float Head { get; init; }

    [JsonPropertyName("neck")]
    public float Neck { get; init; }

    [JsonPropertyName("chest")]
    public float Chest { get; init; }

    [JsonPropertyName("stomach")]
    public float Stomach { get; init; }

    [JsonPropertyName("upper_arm")]
    public float UpperArm { get; init; }

    [JsonPropertyName("lower_arm")]
    public float LowerArm { get; init; }

    [JsonPropertyName("upper_leg")]
    public float UpperLeg { get; init; }

    [JsonPropertyName("lower_leg")]
    public float LowerLeg { get; init; }
}

public sealed class BalanceAttachmentSnapshot
{
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Properties { get; init; }
}

internal static class BalanceSnapshotCatalog
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly Lazy<IReadOnlyList<BalanceSnapshot>> AllSnapshots = new(LoadSnapshots);
    private static readonly Lazy<IReadOnlyDictionary<string, BalanceSnapshot>> SnapshotsByHash =
        new(() => AllSnapshots.Value.ToDictionary(snapshot => snapshot.Hash, StringComparer.OrdinalIgnoreCase));
    private static readonly Lazy<BalanceSnapshot> LatestSnapshot =
        new(() => AllSnapshots.Value.OrderBy(snapshot => snapshot.Version, Comparer<string>.Create(CompareVersion)).Last());

    public static BalanceSnapshot GetLatest() => LatestSnapshot.Value;

    public static BalanceSnapshot GetByHash(string? hash)
    {
        if (string.IsNullOrWhiteSpace(hash))
            return GetLatest();

        if (SnapshotsByHash.Value.TryGetValue(hash, out var snapshot))
            return snapshot;

        throw new InvalidOperationException($"Balance snapshot '{hash}' was not found in embedded balances resources.");
    }

    private static IReadOnlyList<BalanceSnapshot> LoadSnapshots()
    {
        var assembly = typeof(BalanceSnapshotCatalog).Assembly;
        var resourceNames = assembly.GetManifestResourceNames()
            .Where(name => name.Contains("balances", StringComparison.OrdinalIgnoreCase) &&
                           name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        if (resourceNames.Length == 0)
            throw new InvalidOperationException("No embedded balance snapshots were found.");

        var snapshots = new List<BalanceSnapshot>(resourceNames.Length);
        foreach (var resourceName in resourceNames)
        {
            using var stream = assembly.GetManifestResourceStream(resourceName)
                ?? throw new InvalidOperationException($"Embedded balance snapshot '{resourceName}' could not be opened.");
            var snapshot = JsonSerializer.Deserialize<BalanceSnapshot>(stream, JsonOptions)
                ?? throw new InvalidOperationException($"Embedded balance snapshot '{resourceName}' is invalid.");

            if (string.IsNullOrWhiteSpace(snapshot.Version))
                throw new InvalidOperationException($"Embedded balance snapshot '{resourceName}' is missing a version.");
            if (string.IsNullOrWhiteSpace(snapshot.Hash))
                throw new InvalidOperationException($"Embedded balance snapshot '{resourceName}' is missing a hash.");
            if (snapshot.Weapons.Count == 0)
                throw new InvalidOperationException($"Embedded balance snapshot '{resourceName}' does not define any weapons.");

            snapshot = WithNormalizedDictionaries(snapshot);
            snapshots.Add(snapshot);
        }

        return snapshots;
    }

    private static BalanceSnapshot WithNormalizedDictionaries(BalanceSnapshot snapshot)
    {
        return new BalanceSnapshot
        {
            Version = snapshot.Version,
            Hash = snapshot.Hash,
            Weapons = new Dictionary<string, BalanceWeaponSnapshot>(snapshot.Weapons, StringComparer.OrdinalIgnoreCase),
            Attachments = new Dictionary<string, BalanceAttachmentSnapshot>(snapshot.Attachments, StringComparer.OrdinalIgnoreCase)
        };
    }

    private static int CompareVersion(string? left, string? right)
    {
        if (DateOnly.TryParse(left, out var leftDate) && DateOnly.TryParse(right, out var rightDate))
            return leftDate.CompareTo(rightDate);

        if (Version.TryParse(left, out var leftVersion) && Version.TryParse(right, out var rightVersion))
            return leftVersion.CompareTo(rightVersion);

        return StringComparer.Ordinal.Compare(left, right);
    }
}
