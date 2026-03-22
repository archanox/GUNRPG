using System.Text.Json;
using GUNRPG.Application.Backend;
using GUNRPG.Application.Dtos;
using GUNRPG.Application.Mapping;
using GUNRPG.Application.Sessions;

namespace GUNRPG.Application.Combat;

public sealed class OfflineCombatReplayResult
{
    public CombatSessionDto FinalSession { get; init; } = default!;
    public CombatSessionSnapshot FinalSnapshot { get; init; } = default!;
    public CombatOutcome Outcome { get; init; } = default!;
}

public static class OfflineCombatReplay
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static string SerializeCombatSnapshot(CombatSessionSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return JsonSerializer.Serialize(snapshot, JsonOptions);
    }

    public static string ComputeCombatSnapshotHash(CombatSessionDto session)
    {
        ArgumentNullException.ThrowIfNull(session);
        return OfflineMissionHashing.ComputeSnapshotHash(JsonSerializer.Serialize(session, JsonOptions));
    }

    public static CombatSessionSnapshot DeserializeCombatSnapshot(string snapshotJson)
    {
        if (string.IsNullOrWhiteSpace(snapshotJson))
        {
            throw new InvalidOperationException("Offline combat replay snapshot is missing.");
        }

        var snapshot = JsonSerializer.Deserialize<CombatSessionSnapshot>(snapshotJson, JsonOptions);
        return snapshot ?? throw new InvalidOperationException("Offline combat replay snapshot is invalid.");
    }

    public static Task<OfflineCombatReplayResult> ReplayAsync(
        string initialCombatSnapshotJson,
        IReadOnlyList<IntentSnapshot> replayTurns)
    {
        ArgumentNullException.ThrowIfNull(replayTurns);

        var initialSnapshot = DeserializeCombatSnapshot(initialCombatSnapshotJson);

        // Reconstruct the session directly from the initial snapshot and replay every turn using
        // the service's shared helper.  This avoids the circular call chain that would arise if
        // ReplayAsync used CombatSessionService.SubmitPlayerIntentsAsync, because SubmitPlayerIntentsAsync
        // calls ReplayAsync (via FinalizeAsync) for completed sessions.  ExecuteReplayTurn never
        // calls FinalizeAsync, so no recursion occurs.
        var session = SessionMapping.FromSnapshot(initialSnapshot);
        // Set the initial snapshot JSON so the replayed session carries replay integrity metadata.
        session.SetReplayInitialSnapshotJson(initialCombatSnapshotJson);

        foreach (var turn in replayTurns)
        {
            CombatSessionService.ExecuteReplayTurn(session, turn);
            if (session.Phase == SessionPhase.Completed) break;
        }

        if (session.Phase != SessionPhase.Completed)
        {
            throw new InvalidOperationException(
                $"Offline combat replay did not complete " +
                $"({replayTurns.Count} turns replayed, final phase: {session.Phase}).");
        }

        var finalSnapshot = SessionMapping.ToSnapshot(session);
        // Both outcome and FinalSession are derived from the same authoritative post-replay state.
        // The DTO is produced from the snapshot (not the live session) so that BattleLog is empty,
        // consistent with GetStateAsync which reconstructs from a snapshot where ExecutedEvents
        // is ephemeral and not persisted.
        var finalSession = SessionMapping.ToDtoFromSnapshot(finalSnapshot);
        var outcome = session.GetOutcome();

        return Task.FromResult(new OfflineCombatReplayResult
        {
            FinalSession = finalSession,
            FinalSnapshot = finalSnapshot,
            Outcome = outcome
        });
    }

    public static OperatorDto ProjectOperatorResult(OperatorDto initialOperator, CombatOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(initialOperator);
        ArgumentNullException.ThrowIfNull(outcome);

        var projected = CloneOperator(initialOperator);

        if (outcome.OperatorDied)
        {
            projected.CurrentHealth = projected.MaxHealth;
            projected.ExfilStreak = 0;
            projected.IsDead = false;
            projected.CurrentMode = "Base";
            projected.ActiveCombatSessionId = null;
            projected.InfilSessionId = null;
            projected.InfilStartTime = null;
            projected.LockedLoadout = string.Empty;
            projected.EquippedWeaponName = string.Empty;
            return projected;
        }

        if (outcome.XpGained > 0)
        {
            projected.TotalXp += outcome.XpGained;
        }

        projected.ActiveCombatSessionId = null;
        projected.IsDead = false;

        if (outcome.IsVictory)
        {
            projected.CurrentMode = "Infil";
            return projected;
        }

        projected.CurrentMode = "Base";
        projected.InfilSessionId = null;
        projected.InfilStartTime = null;
        projected.ExfilStreak = 0;
        projected.LockedLoadout = string.Empty;
        projected.EquippedWeaponName = string.Empty;
        return projected;
    }

    private static OperatorDto CloneOperator(OperatorDto source)
    {
        return new OperatorDto
        {
            Id = source.Id ?? string.Empty,
            Name = source.Name ?? string.Empty,
            TotalXp = source.TotalXp,
            CurrentHealth = source.CurrentHealth,
            MaxHealth = source.MaxHealth,
            EquippedWeaponName = source.EquippedWeaponName ?? string.Empty,
            UnlockedPerks = (source.UnlockedPerks ?? []).ToList(),
            ExfilStreak = source.ExfilStreak,
            IsDead = source.IsDead,
            CurrentMode = source.CurrentMode ?? string.Empty,
            ActiveCombatSessionId = source.ActiveCombatSessionId,
            InfilSessionId = source.InfilSessionId,
            InfilStartTime = source.InfilStartTime,
            LockedLoadout = source.LockedLoadout ?? string.Empty,
            Pet = source.Pet == null
                ? null
                : new PetStateDto
                {
                    Health = source.Pet.Health,
                    Fatigue = source.Pet.Fatigue,
                    Injury = source.Pet.Injury,
                    Stress = source.Pet.Stress,
                    Morale = source.Pet.Morale,
                    Hunger = source.Pet.Hunger,
                    Hydration = source.Pet.Hydration,
                    LastUpdated = source.Pet.LastUpdated
                }
        };
    }

}
