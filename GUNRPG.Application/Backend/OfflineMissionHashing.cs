using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GUNRPG.Application.Dtos;
using GUNRPG.Core.Operators;

namespace GUNRPG.Application.Backend;

public static class OfflineMissionHashing
{
    private static readonly JsonSerializerOptions ReplayValidationSerializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public static string ComputeOperatorStateHash(OperatorDto dto)
    {
        return ComputeHash(new OperatorHashState(
            dto.Id,
            dto.Name,
            dto.TotalXp,
            dto.CurrentHealth,
            dto.MaxHealth,
            dto.EquippedWeaponName,
            dto.UnlockedPerks.OrderBy(x => x, StringComparer.Ordinal).ToArray(),
            dto.ExfilStreak,
            dto.IsDead,
            dto.CurrentMode,
            dto.InfilSessionId,
            dto.InfilStartTime,
            dto.LockedLoadout,
            dto.Pet == null ? null : new PetHashState(dto.Pet.Health, dto.Pet.Fatigue, dto.Pet.Injury, dto.Pet.Stress, dto.Pet.Morale, dto.Pet.Hunger, dto.Pet.Hydration, dto.Pet.LastUpdated)));
    }

    public static byte[] ComputeReplayFinalStateHash(OperatorAggregate aggregate)
    {
        ArgumentNullException.ThrowIfNull(aggregate);

        return SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(
            new ReplayValidationHashState(
                aggregate.Id.Value.ToString(),
                aggregate.Name,
                aggregate.TotalXp,
                aggregate.CurrentHealth,
                aggregate.MaxHealth,
                aggregate.EquippedWeaponName,
                aggregate.UnlockedPerks.OrderBy(x => x, StringComparer.Ordinal).ToArray(),
                aggregate.ExfilStreak,
                aggregate.IsDead,
                aggregate.CurrentMode.ToString(),
                aggregate.InfilSessionId,
                aggregate.InfilStartTime,
                aggregate.ActiveCombatSessionId,
                aggregate.LockedLoadout,
                aggregate.PetState == null
                    ? null
                    : new PetHashState(
                        aggregate.PetState.Health,
                        aggregate.PetState.Fatigue,
                        aggregate.PetState.Injury,
                        aggregate.PetState.Stress,
                        aggregate.PetState.Morale,
                        aggregate.PetState.Hunger,
                        aggregate.PetState.Hydration,
                        aggregate.PetState.LastUpdated)),
            ReplayValidationSerializerOptions));
    }

    public static string ComputeOperatorStateHash(OperatorStateDto dto)
    {
        return ComputeHash(new OperatorHashState(
            dto.Id.ToString(),
            dto.Name,
            dto.TotalXp,
            dto.CurrentHealth,
            dto.MaxHealth,
            dto.EquippedWeaponName,
            dto.UnlockedPerks.OrderBy(x => x, StringComparer.Ordinal).ToArray(),
            dto.ExfilStreak,
            dto.IsDead,
            dto.CurrentMode.ToString(),
            dto.InfilSessionId,
            dto.InfilStartTime,
            dto.LockedLoadout,
            dto.Pet == null ? null : new PetHashState(dto.Pet.Health, dto.Pet.Fatigue, dto.Pet.Injury, dto.Pet.Stress, dto.Pet.Morale, dto.Pet.Hunger, dto.Pet.Hydration, dto.Pet.LastUpdated)));
    }

    /// <summary>
    /// Computes a SHA256 hash over an already-serialized canonical JSON string.
    /// Use this when the caller owns the serialization step (ordered properties, no indentation).
    /// </summary>
    public static string ComputeSnapshotHash(string canonicalJson)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(canonicalJson));
        return Convert.ToHexString(bytes);
    }

    private static string ComputeHash(OperatorHashState state)
    {
        var json = JsonSerializer.Serialize(state);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return Convert.ToHexString(bytes);
    }

    private sealed record OperatorHashState(
        string Id,
        string Name,
        long TotalXp,
        float CurrentHealth,
        float MaxHealth,
        string EquippedWeaponName,
        string[] UnlockedPerks,
        int ExfilStreak,
        bool IsDead,
        string CurrentMode,
        Guid? InfilSessionId,
        DateTimeOffset? InfilStartTime,
        string LockedLoadout,
        PetHashState? Pet);

    private sealed record ReplayValidationHashState(
        string Id,
        string Name,
        long TotalXp,
        float CurrentHealth,
        float MaxHealth,
        string EquippedWeaponName,
        string[] UnlockedPerks,
        int ExfilStreak,
        bool IsDead,
        string CurrentMode,
        Guid? InfilSessionId,
        DateTimeOffset? InfilStartTime,
        Guid? ActiveCombatSessionId,
        string LockedLoadout,
        PetHashState? Pet);

    private sealed record PetHashState(
        float Health,
        float Fatigue,
        float Injury,
        float Stress,
        float Morale,
        float Hunger,
        float Hydration,
        DateTimeOffset LastUpdated);
}
