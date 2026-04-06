using GUNRPG.Application.Combat;
using GUNRPG.Application.Mapping;
using GUNRPG.Application.Requests;
using GUNRPG.Application.Sessions;
using GUNRPG.Core;
using GUNRPG.Core.Intents;

namespace GUNRPG.Tests;

/// <summary>
/// Verifies that the CombatSession FinalHash provides replay integrity:
/// 1. Same InputLog + Seed → identical hash across runs.
/// 2. Live simulation == replayed simulation (FinalHash round-trips persistence).
/// 3. Persisted session → load → replay → same hash.
/// 4. A tampered FinalHash is rejected on load.
/// 5. Completed sessions block further inputs.
/// </summary>
public sealed class CombatSessionReplayIntegrityTests
{
    // ──────────────────────────────────────────────────────────────────────
    // 1. Same inputs + seed → identical FinalHash across runs
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void FinalHash_IsDeterministic_GivenSameInputsAndSeed()
    {
        const int seed = 999;
        var sessionId = Guid.NewGuid();
        var operatorId = Guid.NewGuid();

        var sessionA = CombatSession.CreateDefault(seed: seed, id: sessionId, operatorId: operatorId);
        RecordTwoTurns(sessionA, operatorId);
        sessionA.TransitionTo(SessionPhase.Completed);

        // Rebuild a second session with the same ID, seed, operatorId, and replay turns.
        var sessionB = CombatSession.CreateDefault(seed: seed, id: sessionId, operatorId: operatorId);
        RecordTwoTurns(sessionB, operatorId);
        sessionB.TransitionTo(SessionPhase.Completed);

        Assert.NotNull(sessionA.FinalHash);
        Assert.NotNull(sessionB.FinalHash);
        Assert.True(
            sessionA.FinalHash!.AsSpan().SequenceEqual(sessionB.FinalHash!),
            "FinalHash must be identical when SessionId, Seed, and ReplayTurns are the same.");
    }

    [Fact]
    public void FinalHash_DiffersForDifferentSeeds()
    {
        var id = Guid.NewGuid();
        var operatorId = Guid.NewGuid();

        var sessionA = CombatSession.CreateDefault(seed: 100, id: id, operatorId: operatorId);
        RecordTwoTurns(sessionA, operatorId);
        sessionA.TransitionTo(SessionPhase.Completed);

        var sessionB = CombatSession.CreateDefault(seed: 200, id: id, operatorId: operatorId);
        RecordTwoTurns(sessionB, operatorId);
        sessionB.TransitionTo(SessionPhase.Completed);

        Assert.NotNull(sessionA.FinalHash);
        Assert.NotNull(sessionB.FinalHash);
        Assert.False(
            sessionA.FinalHash!.AsSpan().SequenceEqual(sessionB.FinalHash!),
            "Sessions with different seeds must produce different FinalHashes.");
    }

    // ──────────────────────────────────────────────────────────────────────
    // 2. FinalHash is set after completion; null before
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void FinalHash_IsNull_BeforeCompletion()
    {
        var session = CombatSession.CreateDefault(seed: 42);
        Assert.Null(session.FinalHash);
    }

    [Fact]
    public void FinalHash_IsSet_AfterTransitionToCompleted()
    {
        var session = CombatSession.CreateDefault(seed: 42);
        session.TransitionTo(SessionPhase.Completed);
        Assert.NotNull(session.FinalHash);
        Assert.NotEmpty(session.FinalHash!);
    }

    // ──────────────────────────────────────────────────────────────────────
    // 3. Persistence round-trip: save → load → FinalHash still matches
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PersistedSession_LoadReturnsMatchingFinalHash()
    {
        var store = new InMemoryCombatSessionStore();
        var service = new CombatSessionService(store);

        // Create and persist a session.
        var createResult = await service.CreateSessionAsync(new SessionCreateRequest { Seed = 77 });
        Assert.True(createResult.IsSuccess);
        var sessionId = createResult.Value!.Id;

        // Complete the session by advancing until combat ends.
        await RunSessionToCompletion(service, sessionId);

        // Load the snapshot directly from the store to inspect the persisted FinalHash.
        var snapshot = await store.LoadAsync(sessionId);
        Assert.NotNull(snapshot);

        Assert.Equal(SessionPhase.Completed, snapshot!.Phase);
        Assert.NotNull(snapshot.FinalHash);

        // Recompute the state-based hash from replay output and verify it matches.
        // The stored FinalHash is now hash(replay(ReplayTurns, Seed)), not hash(inputs).
        var session = SessionMapping.FromSnapshot(snapshot);
        var rebuilt = await session.RebuildStateAsync();
        Assert.NotNull(rebuilt);

        var recomputed = CombatSessionHasher.ComputeStateHash(rebuilt!);

        Assert.True(
            recomputed.AsSpan().SequenceEqual(snapshot.FinalHash),
            "Recomputed hash must equal the stored FinalHash.");
    }

    [Fact]
    public async Task LoadAsync_WithValidFinalHash_Succeeds()
    {
        var store = new InMemoryCombatSessionStore();

        // Directly build a completed session snapshot with a valid FinalHash.
        var session = CombatSession.CreateDefault(seed: 12, id: Guid.NewGuid());
        RecordTwoTurns(session);
        session.TransitionTo(SessionPhase.Completed);

        var snapshot = SessionMapping.ToSnapshot(session);
        await store.SaveAsync(snapshot);

        // Service should load it without throwing.
        var service = new CombatSessionService(store);
        var result = await service.GetStateAsync(session.Id);

        Assert.True(result.IsSuccess);
        Assert.Equal(SessionPhase.Completed, result.Value!.Phase);
    }

    // ──────────────────────────────────────────────────────────────────────
    // 4. Tampered FinalHash → service returns NotFound on load
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task LoadAsync_WithTamperedFinalHash_ReturnsNotFound()
    {
        var store = new InMemoryCombatSessionStore();

        // Build a valid completed session.
        var session = CombatSession.CreateDefault(seed: 55, id: Guid.NewGuid());
        RecordTwoTurns(session);
        session.TransitionTo(SessionPhase.Completed);
        var validSnapshot = SessionMapping.ToSnapshot(session);

        await store.SaveAsync(CloneWithTamperedHash(validSnapshot, session.FinalHash!));

        // Sessions with an invalid FinalHash are treated as not found.
        var service = new CombatSessionService(store);
        var result = await service.GetStateAsync(session.Id);
        Assert.False(result.IsSuccess);
    }

    // ──────────────────────────────────────────────────────────────────────
    // 5. Completed session rejects further inputs
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void RecordReplayTurn_ThrowsAfterCompletion()
    {
        var session = CombatSession.CreateDefault(seed: 1);
        session.TransitionTo(SessionPhase.Completed);

        var intents = SimultaneousIntents.CreateStop(session.Player.Id);
        Assert.Throws<InvalidOperationException>(() => session.RecordReplayTurn(intents));
    }

    [Fact]
    public void AdvanceTurnCounter_ThrowsAfterCompletion()
    {
        var session = CombatSession.CreateDefault(seed: 2);
        session.TransitionTo(SessionPhase.Completed);

        Assert.Throws<InvalidOperationException>(() => session.AdvanceTurnCounter());
    }

    // ──────────────────────────────────────────────────────────────────────
    // 6. InMemoryStore isolation — external mutation does not affect store
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task InMemoryStore_Save_IsolatesSnapshot()
    {
        var store = new InMemoryCombatSessionStore();
        var session = CombatSession.CreateDefault(seed: 7);
        RecordTwoTurns(session);
        var snapshot = SessionMapping.ToSnapshot(session);
        await store.SaveAsync(snapshot);

        // Mutate the list that was passed to SaveAsync — stored copy must be unaffected.
        snapshot.ReplayTurns.Clear();

        var loaded = await store.LoadAsync(session.Id);
        Assert.NotNull(loaded);
        Assert.Equal(2, loaded!.ReplayTurns.Count);
    }

    [Fact]
    public async Task InMemoryStore_Load_IsolatesSnapshot()
    {
        var store = new InMemoryCombatSessionStore();
        var session = CombatSession.CreateDefault(seed: 8);
        RecordTwoTurns(session);
        var snapshot = SessionMapping.ToSnapshot(session);
        await store.SaveAsync(snapshot);

        var loaded = await store.LoadAsync(session.Id);
        Assert.NotNull(loaded);

        // Mutate the loaded copy — store must remain unchanged.
        loaded!.ReplayTurns.Clear();

        var loadedAgain = await store.LoadAsync(session.Id);
        Assert.NotNull(loadedAgain);
        Assert.Equal(2, loadedAgain!.ReplayTurns.Count);
    }

    // ──────────────────────────────────────────────────────────────────────
    // 7. CurrentTick tracks TurnNumber
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void CurrentTick_EqualsInitialTurnNumber()
    {
        var session = CombatSession.CreateDefault(seed: 3);
        Assert.Equal(session.TurnNumber, session.CurrentTick);
    }

    [Fact]
    public void CurrentTick_AdvancesWithTurnNumber()
    {
        var session = CombatSession.CreateDefault(seed: 4);
        var initialTick = session.CurrentTick;
        session.AdvanceTurnCounter();
        Assert.Equal(initialTick + 1, session.CurrentTick);
        Assert.Equal(session.TurnNumber, session.CurrentTick);
    }

    [Fact]
    public void Snapshot_CapturesBalanceSnapshotMetadata()
    {
        var session = CombatSession.CreateDefault(seed: 5);

        var snapshot = SessionMapping.ToSnapshot(session);

        Assert.Equal(WeaponFactory.CurrentBalanceVersion, snapshot.BalanceSnapshotVersion);
        Assert.Equal(WeaponFactory.CurrentBalanceHash, snapshot.BalanceSnapshotHash);
    }

    [Fact]
    public void ComputeStateHash_IncludesBalanceSnapshotHash()
    {
        var session = CombatSession.CreateDefault(seed: 6);
        session.TransitionTo(SessionPhase.Completed);
        var snapshot = SessionMapping.ToSnapshot(session);

        var originalHash = CombatSessionHasher.ComputeStateHash(snapshot);
        var modifiedSnapshot = new CombatSessionSnapshot
        {
            Id = snapshot.Id,
            OperatorId = snapshot.OperatorId,
            Phase = snapshot.Phase,
            TurnNumber = snapshot.TurnNumber,
            Combat = snapshot.Combat,
            Player = snapshot.Player,
            Enemy = snapshot.Enemy,
            Pet = snapshot.Pet,
            EnemyLevel = snapshot.EnemyLevel,
            PlayerLevel = snapshot.PlayerLevel,
            Seed = snapshot.Seed,
            PostCombatResolved = snapshot.PostCombatResolved,
            CreatedAt = snapshot.CreatedAt,
            CompletedAt = snapshot.CompletedAt,
            LastActionTimestamp = snapshot.LastActionTimestamp,
            ReplayInitialSnapshotJson = snapshot.ReplayInitialSnapshotJson,
            ReplayTurns = snapshot.ReplayTurns.ToList(),
            BalanceSnapshotVersion = snapshot.BalanceSnapshotVersion,
            BalanceSnapshotHash = "different-balance-hash",
            Version = snapshot.Version,
            FinalHash = snapshot.FinalHash
        };

        var modifiedHash = CombatSessionHasher.ComputeStateHash(modifiedSnapshot);

        Assert.False(originalHash.AsSpan().SequenceEqual(modifiedHash));
    }

    // ──────────────────────────────────────────────────────────────────────
    // 8. RebuildStateAsync: replay turns only → rebuild state matches FinalHash
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RebuildStateAsync_OnCompletedSession_MatchesFinalHash()
    {
        var store = new InMemoryCombatSessionStore();
        var service = new CombatSessionService(store);

        // Create and drive session to completion through the service so that
        // TurnNumber, ReplayTurns, and FinalHash are all coherently set.
        var createResult = await service.CreateSessionAsync(new SessionCreateRequest { Seed = 77 });
        Assert.True(createResult.IsSuccess);
        var sessionId = createResult.Value!.Id;
        await RunSessionToCompletion(service, sessionId);

        var snapshot = await store.LoadAsync(sessionId);
        Assert.NotNull(snapshot);
        Assert.Equal(SessionPhase.Completed, snapshot!.Phase);
        Assert.NotNull(snapshot.FinalHash);

        // Reconstruct session from the stored snapshot, then rebuild from ReplayTurns only.
        var session = SessionMapping.FromSnapshot(snapshot);
        var rebuiltSnapshot = await session.RebuildStateAsync();

        Assert.NotNull(rebuiltSnapshot);

        // The stored FinalHash is hash(replay(ReplayTurns, Seed)).
        // Verify by computing ComputeStateHash on the rebuilt snapshot and comparing.
        var replayStateHash = CombatSessionHasher.ComputeStateHash(rebuiltSnapshot!);
        Assert.True(
            replayStateHash.AsSpan().SequenceEqual(snapshot.FinalHash),
            "Replay-rebuilt state must produce an identical FinalHash to the original.");
    }

    [Fact]
    public void ReplayRunner_Run_GivenSameInitialStateAndTurns_ProducesIdenticalSimulationState()
    {
        var session = CombatSession.CreateDefault(seed: 4242, id: Guid.NewGuid());
        var initialSnapshot = SessionMapping.ToSnapshot(session);
        var initialJson = OfflineCombatReplay.SerializeCombatSnapshot(initialSnapshot);
        var turns = new[]
        {
            new IntentSnapshot { OperatorId = session.Player.Id, Primary = PrimaryAction.Fire },
            new IntentSnapshot { OperatorId = session.Player.Id, Primary = PrimaryAction.Reload }
        };

        var simulationA = ReplayRunner.Run(initialJson, turns);
        var simulationB = ReplayRunner.Run(initialJson, turns);

        AssertSnapshotsEqual(simulationA.Snapshot, simulationB.Snapshot);
        Assert.Equal(simulationA.Outcome?.CompletedAt, simulationB.Outcome?.CompletedAt);
        Assert.Equal(simulationA.SideEffects, simulationB.SideEffects);
    }

    [Fact]
    public async Task ReplayRunner_Run_DoesNotDriftWithWallClockTime()
    {
        var session = CombatSession.CreateDefault(seed: 5150, id: Guid.NewGuid());
        var initialSnapshot = SessionMapping.ToSnapshot(session);
        var initialJson = OfflineCombatReplay.SerializeCombatSnapshot(initialSnapshot);
        var turns = new[]
        {
            new IntentSnapshot { OperatorId = session.Player.Id, Primary = PrimaryAction.Fire }
        };

        var firstRun = ReplayRunner.Run(initialJson, turns);
        await Task.Delay(50);
        var secondRun = ReplayRunner.Run(initialJson, turns);

        Assert.Equal(firstRun.Snapshot.CompletedAt, secondRun.Snapshot.CompletedAt);
        Assert.Equal(firstRun.Snapshot.LastActionTimestamp, secondRun.Snapshot.LastActionTimestamp);
        AssertSnapshotsEqual(firstRun.Snapshot, secondRun.Snapshot);
    }

    [Fact]
    public async Task ReplayRunner_Run_LeavesPetSideEffectsOutsidePureReplay()
    {
        var store = new InMemoryCombatSessionStore();
        var service = new CombatSessionService(store);

        var createResult = await service.CreateSessionAsync(new SessionCreateRequest { Seed = 77 });
        Assert.True(createResult.IsSuccess);
        var sessionId = createResult.Value!.Id;

        await RunSessionToCompletion(service, sessionId);

        var completedSnapshot = await store.LoadAsync(sessionId);
        Assert.NotNull(completedSnapshot);
        Assert.True(completedSnapshot!.PostCombatResolved);

        var initialSnapshot = OfflineCombatReplay.DeserializeCombatSnapshot(completedSnapshot.ReplayInitialSnapshotJson);
        var pureReplay = ReplayRunner.Run(completedSnapshot.ReplayInitialSnapshotJson, completedSnapshot.ReplayTurns);

        Assert.False(pureReplay.Snapshot.PostCombatResolved);
        Assert.Equal(initialSnapshot.Pet.Health, pureReplay.Snapshot.Pet.Health);
        Assert.Equal(initialSnapshot.Pet.Fatigue, pureReplay.Snapshot.Pet.Fatigue);
        Assert.Equal(initialSnapshot.Pet.Stress, pureReplay.Snapshot.Pet.Stress);
        Assert.Equal(initialSnapshot.Pet.LastUpdated, pureReplay.Snapshot.Pet.LastUpdated);

        Assert.NotEqual(completedSnapshot.Pet.Fatigue, pureReplay.Snapshot.Pet.Fatigue);
        Assert.NotEqual(completedSnapshot.Pet.LastUpdated, pureReplay.Snapshot.Pet.LastUpdated);
    }

    [Fact]
    public void ComputeCombatSnapshotHash_IgnoresPetAndBattleLogSideEffects()
    {
        var baseline = new GUNRPG.Application.Dtos.CombatSessionDto
        {
            Id = Guid.NewGuid(),
            Phase = SessionPhase.Completed,
            CurrentTimeMs = 1200,
            EnemyLevel = 3,
            TurnNumber = 4,
            Player = new GUNRPG.Application.Dtos.PlayerStateDto
            {
                Id = Guid.NewGuid(),
                Name = "Player",
                Health = 80,
                MaxHealth = 100,
                CurrentAmmo = 12,
                DistanceToOpponent = 10,
                AimState = GUNRPG.Core.Operators.AimState.Hip,
                MovementState = GUNRPG.Core.Operators.MovementState.Idle,
                CurrentMovement = GUNRPG.Core.Operators.MovementState.Idle,
                CurrentDirection = GUNRPG.Core.Operators.MovementDirection.Holding,
                CurrentCover = GUNRPG.Core.Operators.CoverState.None,
                IsAlive = true
            },
            Enemy = new GUNRPG.Application.Dtos.PlayerStateDto
            {
                Id = Guid.NewGuid(),
                Name = "Enemy",
                Health = 0,
                MaxHealth = 100,
                CurrentAmmo = 3,
                DistanceToOpponent = 10,
                AimState = GUNRPG.Core.Operators.AimState.Hip,
                MovementState = GUNRPG.Core.Operators.MovementState.Idle,
                CurrentMovement = GUNRPG.Core.Operators.MovementState.Idle,
                CurrentDirection = GUNRPG.Core.Operators.MovementDirection.Holding,
                CurrentCover = GUNRPG.Core.Operators.CoverState.None,
                IsAlive = false
            },
            Pet = new GUNRPG.Application.Dtos.PetStateDto
            {
                Health = 100,
                Fatigue = 0,
                Injury = 0,
                Stress = 0,
                Morale = 100,
                Hunger = 0,
                Hydration = 100,
                LastUpdated = DateTimeOffset.Parse("2026-01-01T00:00:00+00:00")
            },
            BattleLog = []
        };

        var mutated = new GUNRPG.Application.Dtos.CombatSessionDto
        {
            Id = baseline.Id,
            Phase = baseline.Phase,
            CurrentTimeMs = baseline.CurrentTimeMs,
            EnemyLevel = baseline.EnemyLevel,
            TurnNumber = baseline.TurnNumber,
            Player = baseline.Player,
            Enemy = baseline.Enemy,
            Pet = new GUNRPG.Application.Dtos.PetStateDto
            {
                Health = 50,
                Fatigue = 40,
                Injury = 10,
                Stress = 30,
                Morale = 75,
                Hunger = 20,
                Hydration = 60,
                LastUpdated = DateTimeOffset.Parse("2026-01-02T00:00:00+00:00")
            },
            BattleLog =
            [
                new GUNRPG.Application.Dtos.BattleLogEntryDto
                {
                    EventType = "Damage",
                    TimeMs = 10,
                    Message = "Side effect only",
                    ActorName = "Player"
                }
            ]
        };

        Assert.Equal(
            OfflineCombatReplay.ComputeCombatSnapshotHash(baseline),
            OfflineCombatReplay.ComputeCombatSnapshotHash(mutated));
    }

    [Fact]
    public async Task RebuildStateAsync_WithNoInitialSnapshotJson_ReturnsNull()
    {
        // A session without a recorded initial snapshot cannot be rebuilt.
        var session = CombatSession.CreateDefault(seed: 5);
        RecordTwoTurns(session);
        session.TransitionTo(SessionPhase.Completed);

        // ReplayInitialSnapshotJson is empty because CreateDefault does not set it.
        var result = await session.RebuildStateAsync();
        Assert.Null(result);
    }

    // ──────────────────────────────────────────────────────────────────────
    // 9. Store-level FinalHash validation (InMemory)
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task InMemoryStore_LoadAsync_WithTamperedFinalHash_ReturnsNull()
    {
        var store = new InMemoryCombatSessionStore();

        // Build a valid completed session snapshot.
        var session = CombatSession.CreateDefault(seed: 55, id: Guid.NewGuid());
        RecordTwoTurns(session);
        session.TransitionTo(SessionPhase.Completed);
        var validSnapshot = SessionMapping.ToSnapshot(session);

        await store.SaveAsync(CloneWithTamperedHash(validSnapshot, session.FinalHash!));

        // The store itself must reject the tampered session at the storage layer.
        var loaded = await store.LoadAsync(session.Id);
        Assert.Null(loaded);
    }

    [Fact]
    public async Task InMemoryStore_LoadAsync_InProgressSession_DoesNotValidateHash()
    {
        // In-progress sessions have no FinalHash; the store must not reject them.
        var store = new InMemoryCombatSessionStore();
        var session = CombatSession.CreateDefault(seed: 6);
        RecordTwoTurns(session);
        var snapshot = SessionMapping.ToSnapshot(session);
        await store.SaveAsync(snapshot);

        var loaded = await store.LoadAsync(session.Id);
        Assert.NotNull(loaded);
    }

    // ──────────────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────────────

    private static void RecordTwoTurns(CombatSession session, Guid? operatorId = null)
    {
        var id = operatorId ?? session.Player.Id;
        var intentsA = new SimultaneousIntents(id) { Primary = PrimaryAction.Fire };
        var intentsB = new SimultaneousIntents(id) { Primary = PrimaryAction.None };
        session.RecordReplayTurn(intentsA);
        session.RecordReplayTurn(intentsB);
    }

    /// <summary>
    /// Returns a copy of <paramref name="snapshot"/> with a single flipped byte in the FinalHash,
    /// simulating tampering while keeping all other fields identical.
    /// </summary>
    private static CombatSessionSnapshot CloneWithTamperedHash(CombatSessionSnapshot snapshot, byte[] originalHash)
    {
        var tamperedHash = (byte[])originalHash.Clone();
        tamperedHash[0] ^= 0xFF;
        return new CombatSessionSnapshot
        {
            Id = snapshot.Id,
            OperatorId = snapshot.OperatorId,
            Phase = snapshot.Phase,
            TurnNumber = snapshot.TurnNumber,
            Combat = snapshot.Combat,
            Player = snapshot.Player,
            Enemy = snapshot.Enemy,
            Pet = snapshot.Pet,
            EnemyLevel = snapshot.EnemyLevel,
            Seed = snapshot.Seed,
            PostCombatResolved = snapshot.PostCombatResolved,
            CreatedAt = snapshot.CreatedAt,
            CompletedAt = snapshot.CompletedAt,
            LastActionTimestamp = snapshot.LastActionTimestamp,
            ReplayInitialSnapshotJson = snapshot.ReplayInitialSnapshotJson,
            ReplayTurns = snapshot.ReplayTurns,
            Version = snapshot.Version,
            FinalHash = tamperedHash,
        };
    }

    /// <summary>
    /// Drives an existing session forward via the service until it either completes or exceeds a turn limit.
    /// Uses only SubmitPlayerIntentsAsync — no separate Advance step is required.
    /// </summary>
    private static async Task RunSessionToCompletion(CombatSessionService service, Guid sessionId, int maxTurns = 30)
    {
        for (var i = 0; i < maxTurns; i++)
        {
            var stateResult = await service.GetStateAsync(sessionId);
            if (!stateResult.IsSuccess) break;
            if (stateResult.Value!.Phase == SessionPhase.Completed) return;

            var submitResult = await service.SubmitPlayerIntentsAsync(sessionId, new SubmitIntentsRequest
            {
                Intents = new Application.Dtos.IntentDto { Primary = PrimaryAction.Fire }
            });
            if (!submitResult.IsSuccess) break;
            if (submitResult.Value!.Phase == SessionPhase.Completed) return;
        }
    }

    private static void AssertSnapshotsEqual(CombatSessionSnapshot expected, CombatSessionSnapshot actual)
    {
        Assert.Equal(expected.Id, actual.Id);
        Assert.Equal(expected.Phase, actual.Phase);
        Assert.Equal(expected.TurnNumber, actual.TurnNumber);
        Assert.Equal(expected.Combat.Phase, actual.Combat.Phase);
        Assert.Equal(expected.Combat.CurrentTimeMs, actual.Combat.CurrentTimeMs);
        Assert.Equal(expected.Combat.RandomState.Seed, actual.Combat.RandomState.Seed);
        Assert.Equal(expected.Combat.RandomState.CallCount, actual.Combat.RandomState.CallCount);
        Assert.Equal(expected.Player.Health, actual.Player.Health);
        Assert.Equal(expected.Player.CurrentAmmo, actual.Player.CurrentAmmo);
        Assert.Equal(expected.Enemy.Health, actual.Enemy.Health);
        Assert.Equal(expected.Enemy.CurrentAmmo, actual.Enemy.CurrentAmmo);
        Assert.Equal(expected.CompletedAt, actual.CompletedAt);
        Assert.Equal(expected.LastActionTimestamp, actual.LastActionTimestamp);
    }
}
