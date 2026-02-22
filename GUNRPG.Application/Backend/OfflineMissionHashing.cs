using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GUNRPG.Application.Dtos;

namespace GUNRPG.Application.Backend;

public static class OfflineMissionHashing
{
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
