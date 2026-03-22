using GUNRPG.Application.Combat;
using GUNRPG.Application.Mapping;
using GUNRPG.Core.VirtualPet;

namespace GUNRPG.Application.Sessions;

/// <summary>
/// Pure replay output: deterministic simulation state plus side-effect instructions that can be
/// applied later without re-running combat logic.
/// </summary>
public sealed class ReplaySimulationState
{
    public CombatSession Session { get; init; } = default!;
    public CombatSessionSnapshot Snapshot { get; init; } = default!;
    public CombatOutcome? Outcome { get; init; }
    public CombatReplaySideEffectPlan SideEffects { get; init; } = CombatReplaySideEffectPlan.None;
}

/// <summary>
/// Side effects derived from combat replay. Pet and operator progression remain outside replay and
/// are not part of deterministic validation or hashing.
/// </summary>
public sealed record CombatReplaySideEffectPlan(
    bool RequiresApplication,
    bool PlayerSurvived,
    MissionInput? PetMissionInput,
    DateTimeOffset? MissionResolvedAt)
{
    public static CombatReplaySideEffectPlan None { get; } = new(false, false, null, null);
}

/// <summary>
/// Runs combat replay as a pure deterministic transformation:
/// (ReplayTurns + Seed + InitialState) -> SimulationState.
/// No side effects are applied here.
/// </summary>
public static class ReplayRunner
{
    public static ReplaySimulationState Run(
        string initialCombatSnapshotJson,
        IReadOnlyList<IntentSnapshot> replayTurns)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(initialCombatSnapshotJson);
        ArgumentNullException.ThrowIfNull(replayTurns);

        var initialSnapshot = OfflineCombatReplay.DeserializeCombatSnapshot(initialCombatSnapshotJson);
        return Run(initialSnapshot, replayTurns, initialCombatSnapshotJson);
    }

    public static ReplaySimulationState Run(
        CombatSessionSnapshot initialSnapshot,
        IReadOnlyList<IntentSnapshot> replayTurns,
        string? replayInitialSnapshotJson = null)
    {
        ArgumentNullException.ThrowIfNull(initialSnapshot);
        ArgumentNullException.ThrowIfNull(replayTurns);

        var session = SessionMapping.FromSnapshot(initialSnapshot);
        if (!string.IsNullOrEmpty(replayInitialSnapshotJson))
        {
            session.SetReplayInitialSnapshotJson(replayInitialSnapshotJson);
        }

        foreach (var turn in replayTurns)
        {
            CombatSessionService.ExecuteReplayTurn(session, turn);
            if (session.Phase == SessionPhase.Completed)
            {
                break;
            }
        }

        return new ReplaySimulationState
        {
            Session = session,
            Snapshot = SessionMapping.ToSnapshot(session),
            Outcome = session.Phase == SessionPhase.Completed ? session.GetOutcome() : null,
            SideEffects = CombatSessionService.BuildSideEffectPlan(session)
        };
    }
}
