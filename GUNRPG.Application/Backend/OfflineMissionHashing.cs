using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GUNRPG.Application.Dtos;
using GUNRPG.Core.Operators;

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

    public static byte[] ComputeReplayFinalStateHash(OperatorAggregate aggregate)
    {
        ArgumentNullException.ThrowIfNull(aggregate);

        using var buffer = new MemoryStream();
        using var writer = new BinaryWriter(buffer, Encoding.UTF8, leaveOpen: true);

        WriteString(writer, aggregate.Id.Value.ToString());
        WriteString(writer, aggregate.Name);
        writer.Write(aggregate.TotalXp);
        writer.Write(BitConverter.SingleToInt32Bits(aggregate.CurrentHealth));
        writer.Write(BitConverter.SingleToInt32Bits(aggregate.MaxHealth));
        WriteString(writer, aggregate.EquippedWeaponName);

        var orderedPerks = aggregate.UnlockedPerks.ToArray();
        Array.Sort(orderedPerks, StringComparer.Ordinal);
        writer.Write(orderedPerks.Length);
        foreach (var perk in orderedPerks)
        {
            WriteString(writer, perk);
        }

        writer.Write(aggregate.ExfilStreak);
        writer.Write(aggregate.IsDead);
        WriteString(writer, aggregate.CurrentMode.ToString());
        WriteNullableGuid(writer, aggregate.InfilSessionId);
        WriteNullableDateTimeOffset(writer, aggregate.InfilStartTime);
        WriteNullableGuid(writer, aggregate.ActiveCombatSessionId);
        WriteString(writer, aggregate.LockedLoadout);

        writer.Write(aggregate.PetState != null);
        if (aggregate.PetState != null)
        {
            writer.Write(BitConverter.SingleToInt32Bits(aggregate.PetState.Health));
            writer.Write(BitConverter.SingleToInt32Bits(aggregate.PetState.Fatigue));
            writer.Write(BitConverter.SingleToInt32Bits(aggregate.PetState.Injury));
            writer.Write(BitConverter.SingleToInt32Bits(aggregate.PetState.Stress));
            writer.Write(BitConverter.SingleToInt32Bits(aggregate.PetState.Morale));
            writer.Write(BitConverter.SingleToInt32Bits(aggregate.PetState.Hunger));
            writer.Write(BitConverter.SingleToInt32Bits(aggregate.PetState.Hydration));
            WriteDateTimeOffset(writer, aggregate.PetState.LastUpdated);
        }

        writer.Flush();
        return SHA256.HashData(buffer.GetBuffer().AsSpan(0, checked((int)buffer.Length)));
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

    private sealed record PetHashState(
        float Health,
        float Fatigue,
        float Injury,
        float Stress,
        float Morale,
        float Hunger,
        float Hydration,
        DateTimeOffset LastUpdated);

    private static void WriteString(BinaryWriter writer, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        writer.Write(bytes.Length);
        writer.Write(bytes);
    }

    private static void WriteNullableGuid(BinaryWriter writer, Guid? value)
    {
        writer.Write(value.HasValue);
        if (!value.HasValue)
        {
            return;
        }

        Span<byte> guidBytes = stackalloc byte[16];
        value.Value.TryWriteBytes(guidBytes);
        writer.Write(guidBytes);
    }

    private static void WriteNullableDateTimeOffset(BinaryWriter writer, DateTimeOffset? value)
    {
        writer.Write(value.HasValue);
        if (!value.HasValue)
        {
            return;
        }

        WriteDateTimeOffset(writer, value.Value);
    }

    private static void WriteDateTimeOffset(BinaryWriter writer, DateTimeOffset value)
    {
        writer.Write(value.UtcTicks);
        writer.Write(value.Offset.Ticks);
    }
}
