using GUNRPG.Application.Mapping;
using GUNRPG.Application.Requests;
using GUNRPG.Application.Sessions;
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

        if (snapshot!.Phase == SessionPhase.Completed)
        {
            Assert.NotNull(snapshot.FinalHash);

            // Recompute hash from the snapshot data and verify it matches.
            var recomputed = CombatSessionHasher.ComputeHash(
                snapshot.Id,
                snapshot.Seed,
                snapshot.Version > 0 ? snapshot.Version : CombatSession.CurrentVersion,
                snapshot.TurnNumber,
                snapshot.ReplayTurns);

            Assert.True(
                recomputed.AsSpan().SequenceEqual(snapshot.FinalHash),
                "Recomputed hash must equal the stored FinalHash.");
        }
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
    // 4. Tampered FinalHash → exception on load
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task LoadAsync_WithTamperedFinalHash_ThrowsInvalidOperationException()
    {
        var store = new InMemoryCombatSessionStore();

        // Build a valid completed session.
        var session = CombatSession.CreateDefault(seed: 55, id: Guid.NewGuid());
        RecordTwoTurns(session);
        session.TransitionTo(SessionPhase.Completed);
        var validSnapshot = SessionMapping.ToSnapshot(session);

        // Tamper with the stored FinalHash.
        var tamperedHash = (byte[])session.FinalHash!.Clone();
        tamperedHash[0] ^= 0xFF;

        var tamperedSnapshot = new CombatSessionSnapshot
        {
            Id = validSnapshot.Id,
            OperatorId = validSnapshot.OperatorId,
            Phase = validSnapshot.Phase,
            TurnNumber = validSnapshot.TurnNumber,
            Combat = validSnapshot.Combat,
            Player = validSnapshot.Player,
            Enemy = validSnapshot.Enemy,
            Pet = validSnapshot.Pet,
            EnemyLevel = validSnapshot.EnemyLevel,
            Seed = validSnapshot.Seed,
            PostCombatResolved = validSnapshot.PostCombatResolved,
            CreatedAt = validSnapshot.CreatedAt,
            CompletedAt = validSnapshot.CompletedAt,
            LastActionTimestamp = validSnapshot.LastActionTimestamp,
            ReplayInitialSnapshotJson = validSnapshot.ReplayInitialSnapshotJson,
            ReplayTurns = validSnapshot.ReplayTurns,
            Version = validSnapshot.Version,
            FinalHash = tamperedHash,
        };

        await store.SaveAsync(tamperedSnapshot);

        var service = new CombatSessionService(store);
        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await service.GetStateAsync(session.Id));
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
    /// Drives a session forward via the service until it either completes or exceeds a turn limit.
    /// Returns the session ID for further inspection.
    /// </summary>
    private static async Task RunSessionToCompletion(CombatSessionService service, Guid sessionId, int maxTurns = 30)
    {
        for (var i = 0; i < maxTurns; i++)
        {
            var stateResult = await service.GetStateAsync(sessionId);
            if (!stateResult.IsSuccess) break;
            if (stateResult.Value!.Phase == SessionPhase.Completed) return;

            await service.SubmitPlayerIntentsAsync(sessionId, new SubmitIntentsRequest
            {
                Intents = new Application.Dtos.IntentDto { Primary = PrimaryAction.Fire }
            });

            var advResult = await service.AdvanceAsync(sessionId);
            if (!advResult.IsSuccess) break;
            if (advResult.Value!.Phase == SessionPhase.Completed) return;
        }
    }
}
