using System.Text.Json;
using GUNRPG.Application.Backend;
using GUNRPG.Application.Dtos;
using GUNRPG.Application.Requests;
using GUNRPG.Application.Results;
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

    public static async Task<OfflineCombatReplayResult> ReplayAsync(
        string initialCombatSnapshotJson,
        IReadOnlyList<IntentSnapshot> replayTurns)
    {
        ArgumentNullException.ThrowIfNull(replayTurns);

        var initialSnapshot = DeserializeCombatSnapshot(initialCombatSnapshotJson);
        var store = new InMemoryCombatSessionStore();
        await store.SaveAsync(initialSnapshot);

        var sessionService = new CombatSessionService(store);
        foreach (var turn in replayTurns)
        {
            var submitResult = await sessionService.SubmitPlayerIntentsAsync(initialSnapshot.Id, new SubmitIntentsRequest
            {
                OperatorId = turn.OperatorId == Guid.Empty ? null : turn.OperatorId,
                Intents = new IntentDto
                {
                    Primary = turn.Primary,
                    Movement = turn.Movement,
                    Stance = turn.Stance,
                    Cover = turn.Cover,
                    CancelMovement = turn.CancelMovement
                }
            });

            if (submitResult.Status != ResultStatus.Success)
            {
                throw new InvalidOperationException($"Offline combat replay intent submission failed: {submitResult.ErrorMessage}");
            }

            var advanceResult = await sessionService.AdvanceAsync(initialSnapshot.Id, turn.OperatorId == Guid.Empty ? null : turn.OperatorId);
            if (advanceResult.Status != ResultStatus.Success)
            {
                throw new InvalidOperationException($"Offline combat replay advance failed: {advanceResult.ErrorMessage}");
            }
        }

        var finalSession = await sessionService.GetStateAsync(initialSnapshot.Id);
        if (finalSession.Status != ResultStatus.Success || finalSession.Value == null)
        {
            throw new InvalidOperationException(finalSession.ErrorMessage ?? "Offline combat replay did not produce a final session.");
        }

        var finalSnapshot = await store.LoadAsync(initialSnapshot.Id)
            ?? throw new InvalidOperationException("Offline combat replay snapshot was not persisted.");

        var outcome = await sessionService.GetCombatOutcomeAsync(initialSnapshot.Id);
        if (outcome.Status != ResultStatus.Success || outcome.Value == null)
        {
            throw new InvalidOperationException(outcome.ErrorMessage ?? "Offline combat replay did not produce a combat outcome.");
        }

        return new OfflineCombatReplayResult
        {
            FinalSession = finalSession.Value,
            FinalSnapshot = finalSnapshot,
            Outcome = outcome.Value
        };
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
            Id = source.Id,
            Name = source.Name,
            TotalXp = source.TotalXp,
            CurrentHealth = source.CurrentHealth,
            MaxHealth = source.MaxHealth,
            EquippedWeaponName = source.EquippedWeaponName,
            UnlockedPerks = source.UnlockedPerks.ToList(),
            ExfilStreak = source.ExfilStreak,
            IsDead = source.IsDead,
            CurrentMode = source.CurrentMode,
            ActiveCombatSessionId = source.ActiveCombatSessionId,
            InfilSessionId = source.InfilSessionId,
            InfilStartTime = source.InfilStartTime,
            LockedLoadout = source.LockedLoadout,
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
