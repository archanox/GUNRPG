using System.Text.Json;
using GUNRPG.Application.Dtos;
using GUNRPG.Application.Backend;
using GUNRPG.Application.Combat;
using GUNRPG.Application.Operators;
using GUNRPG.Application.Requests;
using GUNRPG.Application.Results;
using GUNRPG.Application.Services;
using GUNRPG.Application.Sessions;
using GUNRPG.Core.Combat;
using GUNRPG.Core.Intents;
using GUNRPG.Core.Operators;
using GUNRPG.Infrastructure.Persistence;
using LiteDB;
using Xunit;

namespace GUNRPG.Tests;

/// <summary>
/// Tests for OperatorService, focusing on the interaction between operators and combat sessions.
/// </summary>
public sealed class OperatorServiceTests : IDisposable
{
    private readonly LiteDatabase _database;
    private readonly LiteDbOperatorEventStore _operatorStore;
    private readonly LiteDbCombatSessionStore _sessionStore;
    private readonly OperatorExfilService _exfilService;
    private readonly CombatSessionService _sessionService;
    private readonly OperatorService _operatorService;

    /// <summary>Minutes past the 30-minute infil window used to create backdated expired infils in tests.</summary>
    private const int ExpiredInfilMinutes = -(OperatorExfilService.InfilTimerMinutes + 1);

    public OperatorServiceTests()
    {
        _database = new LiteDatabase(":memory:");
        _operatorStore = new LiteDbOperatorEventStore(_database);
        _sessionStore = new LiteDbCombatSessionStore(_database);
        _exfilService = new OperatorExfilService(_operatorStore);
        _sessionService = new CombatSessionService(_sessionStore, _operatorStore);
        _operatorService = new OperatorService(_exfilService, _sessionService, _operatorStore);
    }

    public void Dispose()
    {
        _database.Dispose();
    }

    /// <summary>
    /// Helper method to start a combat session using the atomic server-side creation.
    /// The server now creates both the operator event and the session record in one call.
    /// </summary>
    private async Task<Guid> StartAndCreateCombatSessionAsync(Guid operatorId)
    {
        var startCombatResult = await _operatorService.StartCombatSessionAsync(operatorId);
        if (!startCombatResult.IsSuccess)
            throw new InvalidOperationException($"Failed to start combat session: {startCombatResult.ErrorMessage}");
        
        return startCombatResult.Value!;
    }

    [Fact]
    public async Task CleanupCompletedSessionAsync_WithCompletedSession_ProcessesOutcome()
    {
        // Arrange: Create operator and start infil
        var createResult = await _operatorService.CreateOperatorAsync(new OperatorCreateRequest { Name = "TestOp" });
        Assert.True(createResult.IsSuccess);
        var operatorId = createResult.Value!.Id;

        var infilResult = await _operatorService.StartInfilAsync(operatorId);
        Assert.True(infilResult.IsSuccess);
        
        // Start and create combat session
        var sessionId = await StartAndCreateCombatSessionAsync(operatorId);

        // Complete the combat session (simulate victory)
        for (int i = 0; i < 20; i++)
        {
            var submitResult = await _sessionService.SubmitPlayerIntentsAsync(sessionId, new SubmitIntentsRequest
            {
                Intents = new IntentDto { Primary = PrimaryAction.Fire }
            });
            Assert.True(submitResult.IsSuccess);

            var advanceResult = await _sessionService.AdvanceAsync(sessionId);
            Assert.True(advanceResult.IsSuccess);

            if (advanceResult.Value!.Phase == SessionPhase.Completed)
            {
                break;
            }
        }

        // Verify session is completed
        var sessionState = await _sessionService.GetStateAsync(sessionId);
        Assert.True(sessionState.IsSuccess);
        Assert.Equal(SessionPhase.Completed, sessionState.Value!.Phase);

        // Act: Cleanup completed session
        var cleanupResult = await _operatorService.CleanupCompletedSessionAsync(operatorId);
        Assert.True(cleanupResult.IsSuccess);

        // Get operator after cleanup
        var operatorResult = await _operatorService.GetOperatorAsync(operatorId);

        // Assert: Outcome should be processed
        Assert.True(operatorResult.IsSuccess);
        var op = operatorResult.Value!;

        // ActiveSessionId should be cleared after outcome processing
        Assert.Null(op.ActiveCombatSessionId);
        
        // Operator should still be in Infil mode (victory keeps you in infil)
        Assert.Equal(OperatorMode.Infil, op.CurrentMode);
        
        // Exfil streak is NOT incremented on combat victory (only on infil completion)
        Assert.Equal(0, op.ExfilStreak);
        
        // ActiveCombatSession should be null (outcome processed, session reference cleared)
        Assert.Null(op.ActiveCombatSession);
    }

    [Fact]
    public async Task GetOperatorAsync_WithCompletedSession_IncludesSessionInResponse()
    {
        // Arrange: Create operator and start infil
        var createResult = await _operatorService.CreateOperatorAsync(new OperatorCreateRequest { Name = "TestOp" });
        Assert.True(createResult.IsSuccess);
        var operatorId = createResult.Value!.Id;

        var infilResult = await _operatorService.StartInfilAsync(operatorId);
        Assert.True(infilResult.IsSuccess);
        
        // Start and create combat session
        var sessionId = await StartAndCreateCombatSessionAsync(operatorId);

        // Complete the combat session
        for (int i = 0; i < 20; i++)
        {
            var submitResult = await _sessionService.SubmitPlayerIntentsAsync(sessionId, new SubmitIntentsRequest
            {
                Intents = new IntentDto { Primary = PrimaryAction.Fire }
            });
            Assert.True(submitResult.IsSuccess);

            var advanceResult = await _sessionService.AdvanceAsync(sessionId);
            Assert.True(advanceResult.IsSuccess);

            if (advanceResult.Value!.Phase == SessionPhase.Completed)
            {
                break;
            }
        }

        // Act: Get operator WITHOUT cleanup - session should be included in response
        var operatorResult = await _operatorService.GetOperatorAsync(operatorId);

        // Assert: GetOperatorAsync is read-only, doesn't auto-process
        Assert.True(operatorResult.IsSuccess);
        var op = operatorResult.Value!;

        // ActiveSessionId should still be set (not processed yet)
        Assert.Equal(sessionId, op.ActiveCombatSessionId);
        
        // ActiveCombatSession should be included with Completed phase
        Assert.NotNull(op.ActiveCombatSession);
        Assert.Equal(sessionId, op.ActiveCombatSession.Id);
        Assert.Equal(SessionPhase.Completed, op.ActiveCombatSession.Phase);
    }

    [Fact]
    public async Task GetOperatorAsync_WithActiveSession_IncludesSessionInResponse()
    {
        // Arrange: Create operator and start infil
        var createResult = await _operatorService.CreateOperatorAsync(new OperatorCreateRequest { Name = "TestOp" });
        Assert.True(createResult.IsSuccess);
        var operatorId = createResult.Value!.Id;

        var infilResult = await _operatorService.StartInfilAsync(operatorId);
        Assert.True(infilResult.IsSuccess);
        
        // Start and create combat session
        var sessionId = await StartAndCreateCombatSessionAsync(operatorId);

        // Act: Load operator with active (non-completed) session
        var operatorResult = await _operatorService.GetOperatorAsync(operatorId);

        // Assert
        Assert.True(operatorResult.IsSuccess);
        var op = operatorResult.Value!;

        // ActiveSessionId should be set
        Assert.Equal(sessionId, op.ActiveCombatSessionId);
        
        // ActiveCombatSession should be included
        Assert.NotNull(op.ActiveCombatSession);
        Assert.Equal(sessionId, op.ActiveCombatSession.Id);
        
        // Session should be in Planning phase (not completed)
        Assert.Equal(SessionPhase.Planning, op.ActiveCombatSession.Phase);
    }

    [Fact]
    public async Task CleanupCompletedSessionAsync_AfterProcessing_OperatorCanCompleteExfil()
    {
        // Arrange: Create operator, start infil, and complete combat
        var createResult = await _operatorService.CreateOperatorAsync(new OperatorCreateRequest { Name = "TestOp" });
        var operatorId = createResult.Value!.Id;

        var infilResult = await _operatorService.StartInfilAsync(operatorId);
        
        // Start and create combat session
        var sessionId = await StartAndCreateCombatSessionAsync(operatorId);

        // Complete combat
        for (int i = 0; i < 20; i++)
        {
            await _sessionService.SubmitPlayerIntentsAsync(sessionId, new SubmitIntentsRequest
            {
                Intents = new IntentDto { Primary = PrimaryAction.Fire }
            });
            var advanceResult = await _sessionService.AdvanceAsync(sessionId);
            if (advanceResult.Value!.Phase == SessionPhase.Completed)
                break;
        }

        // Cleanup completed session
        await _operatorService.CleanupCompletedSessionAsync(operatorId);

        // Get operator after cleanup
        var operatorAfterCleanup = await _operatorService.GetOperatorAsync(operatorId);

        // Assert: After victory, operator stays in Infil mode but with no active session
        Assert.Equal(OperatorMode.Infil, operatorAfterCleanup.Value!.CurrentMode);
        Assert.Null(operatorAfterCleanup.Value.ActiveCombatSessionId);
        Assert.Equal(0, operatorAfterCleanup.Value.ExfilStreak); // No streak increment from combat victory

        // Act: CompleteExfil should work (this is called when user explicitly exfils after victory)
        var exfilResult = await _exfilService.CompleteExfilAsync(new OperatorId(operatorId));

        // Assert: Exfil completes successfully
        // Note: Per design, operator STAYS in Infil mode after successful exfil.
        // They only return to Base after death or failed exfil.
        Assert.True(exfilResult.IsSuccess);

        var finalState = await _operatorService.GetOperatorAsync(operatorId);
        Assert.Equal(OperatorMode.Infil, finalState.Value!.CurrentMode);  // Still in Infil!
        Assert.Null(finalState.Value.ActiveCombatSessionId);
        Assert.Equal(0, finalState.Value.ExfilStreak); // CompleteExfilAsync emits CombatVictoryEvent which does not increment streak
    }

    [Fact]
    public async Task SyncOfflineMission_WithMissingReplaySnapshot_ReturnsValidationError()
    {
        var createResult = await _operatorService.CreateOperatorAsync(new OperatorCreateRequest { Name = "SyncOp" });
        Assert.True(createResult.IsSuccess);

        var envelope = new OfflineMissionEnvelope
        {
            OperatorId = createResult.Value!.Id.ToString(),
            SequenceNumber = 1,
            RandomSeed = 7,
            InitialSnapshotJson = string.Empty,
            InitialCombatSnapshotJson = "{}",
            FinalCombatSnapshotHash = "hash-c",
            ReplayTurns = [new IntentSnapshot()],
            InitialOperatorStateHash = "hash-a",
            ResultOperatorStateHash = "hash-b",
            FullBattleLog = null!,
            ExecutedUtc = DateTime.UtcNow
        };

        var result = await _operatorService.SyncOfflineMission(envelope);

        Assert.Equal(ResultStatus.ValidationError, result.Status);
    }

    [Fact]
    public async Task SyncOfflineMission_WithInitialSnapshotHashMismatch_ReturnsInvalidState()
    {
        var createResult = await _operatorService.CreateOperatorAsync(new OperatorCreateRequest { Name = "HashOp" });
        Assert.True(createResult.IsSuccess);

        var state = await _operatorService.GetOperatorAsync(createResult.Value!.Id);
        Assert.True(state.IsSuccess);

        var initialOperator = new OperatorDto
        {
            Id = state.Value!.Id.ToString(),
            Name = state.Value.Name,
            TotalXp = state.Value.TotalXp,
            CurrentHealth = state.Value.CurrentHealth,
            MaxHealth = state.Value.MaxHealth,
            EquippedWeaponName = state.Value.EquippedWeaponName,
            UnlockedPerks = state.Value.UnlockedPerks,
            ExfilStreak = state.Value.ExfilStreak,
            IsDead = state.Value.IsDead,
            CurrentMode = state.Value.CurrentMode.ToString(),
            ActiveCombatSessionId = state.Value.ActiveCombatSessionId,
            InfilSessionId = state.Value.InfilSessionId,
            InfilStartTime = state.Value.InfilStartTime,
            LockedLoadout = state.Value.LockedLoadout,
            Pet = state.Value.Pet
        };
        // Compute the envelope hash from the untampered authoritative state, then mutate the
        // serialized snapshot so SyncOfflineMission must reject the snapshot-hash mismatch.
        var currentHash = OfflineMissionHashing.ComputeOperatorStateHash(state.Value);
        initialOperator.Name = "Tampered";

        var result = await _operatorService.SyncOfflineMission(new OfflineMissionEnvelope
        {
            OperatorId = state.Value.Id.ToString(),
            SequenceNumber = 1,
            RandomSeed = 7,
            InitialSnapshotJson = System.Text.Json.JsonSerializer.Serialize(initialOperator),
            InitialCombatSnapshotJson = "{}",
            FinalCombatSnapshotHash = "hash-c",
            ReplayTurns = [new IntentSnapshot()],
            InitialOperatorStateHash = currentHash,
            ResultOperatorStateHash = "hash-b",
            FullBattleLog = [],
            ExecutedUtc = DateTime.UtcNow
        });

        Assert.Equal(ResultStatus.InvalidState, result.Status);
        Assert.Equal("Offline mission envelope initial snapshot hash mismatch", result.ErrorMessage);
    }

    [Fact]
    public async Task SyncOfflineMission_WhenReplayThrows_ReturnsGenericError()
    {
        var createResult = await _operatorService.CreateOperatorAsync(new OperatorCreateRequest { Name = "ReplayOp" });
        Assert.True(createResult.IsSuccess);

        var state = await _operatorService.GetOperatorAsync(createResult.Value!.Id);
        Assert.True(state.IsSuccess);

        var initialOperator = new OperatorDto
        {
            Id = state.Value!.Id.ToString(),
            Name = state.Value.Name,
            TotalXp = state.Value.TotalXp,
            CurrentHealth = state.Value.CurrentHealth,
            MaxHealth = state.Value.MaxHealth,
            EquippedWeaponName = state.Value.EquippedWeaponName,
            UnlockedPerks = state.Value.UnlockedPerks,
            ExfilStreak = state.Value.ExfilStreak,
            IsDead = state.Value.IsDead,
            CurrentMode = state.Value.CurrentMode.ToString(),
            ActiveCombatSessionId = state.Value.ActiveCombatSessionId,
            InfilSessionId = state.Value.InfilSessionId,
            InfilStartTime = state.Value.InfilStartTime,
            LockedLoadout = state.Value.LockedLoadout,
            Pet = state.Value.Pet
        };

        var initialSnapshotHash = OfflineMissionHashing.ComputeOperatorStateHash(initialOperator);
        var result = await _operatorService.SyncOfflineMission(new OfflineMissionEnvelope
        {
            OperatorId = state.Value.Id.ToString(),
            SequenceNumber = 1,
            RandomSeed = 7,
            InitialSnapshotJson = System.Text.Json.JsonSerializer.Serialize(initialOperator),
            InitialCombatSnapshotJson = "not-json",
            FinalCombatSnapshotHash = "hash-c",
            ReplayTurns = [new IntentSnapshot()],
            InitialOperatorStateHash = initialSnapshotHash,
            ResultOperatorStateHash = "hash-b",
            FullBattleLog = [],
            ExecutedUtc = DateTime.UtcNow
        });

        Assert.Equal(ResultStatus.InvalidState, result.Status);
        Assert.Equal("Offline mission replay failed", result.ErrorMessage);
    }

    [Fact]
    public async Task SyncOfflineMission_SequenceTwo_UsesStoredHeadContinuity()
    {
        var storeWithHead = new InMemoryOfflineSyncHeadStore();
        var operatorService = new OperatorService(_exfilService, _sessionService, _operatorStore, storeWithHead);
        var createResult = await operatorService.CreateOperatorAsync(new OperatorCreateRequest { Name = "HeadOp" });
        Assert.True(createResult.IsSuccess);
        var operatorId = createResult.Value!.Id;

        var infilResult = await operatorService.StartInfilAsync(operatorId);
        Assert.True(infilResult.IsSuccess);
        var sessionId = await operatorService.StartCombatSessionAsync(operatorId);
        Assert.True(sessionId.IsSuccess);

        for (var i = 0; i < 20; i++)
        {
            var submitResult = await _sessionService.SubmitPlayerIntentsAsync(sessionId.Value!, new SubmitIntentsRequest
            {
                OperatorId = operatorId,
                Intents = new IntentDto { Primary = PrimaryAction.Fire }
            });
            Assert.True(submitResult.IsSuccess);

            var advanceResult = await _sessionService.AdvanceAsync(sessionId.Value!, operatorId);
            Assert.True(advanceResult.IsSuccess);

            if (advanceResult.Value!.Phase == SessionPhase.Completed)
                break;
        }

        var state = await operatorService.GetOperatorAsync(operatorId);
        Assert.True(state.IsSuccess);
        var initialHash = OfflineMissionHashing.ComputeOperatorStateHash(state.Value!);

        await storeWithHead.UpsertAsync(new OfflineSyncHead
        {
            OperatorId = operatorId,
            SequenceNumber = 1,
            ResultOperatorStateHash = initialHash
        });

        var sessionSnapshot = await _sessionStore.LoadAsync(sessionId.Value!);
        Assert.NotNull(sessionSnapshot);

        var sessionResult = await _sessionService.GetStateAsync(sessionId.Value!);
        var outcomeResult = await _sessionService.GetCombatOutcomeAsync(sessionId.Value!);
        Assert.True(sessionResult.IsSuccess);
        Assert.True(outcomeResult.IsSuccess);
        var replayResult = await OfflineCombatReplay.ReplayAsync(sessionSnapshot.ReplayInitialSnapshotJson, sessionSnapshot.ReplayTurns);

        var initialOperator = new OperatorDto
        {
            Id = operatorId.ToString(),
            Name = state.Value!.Name,
            TotalXp = state.Value.TotalXp,
            CurrentHealth = state.Value.CurrentHealth,
            MaxHealth = state.Value.MaxHealth,
            EquippedWeaponName = state.Value.EquippedWeaponName,
            UnlockedPerks = state.Value.UnlockedPerks,
            ExfilStreak = state.Value.ExfilStreak,
            IsDead = state.Value.IsDead,
            CurrentMode = state.Value.CurrentMode.ToString(),
            ActiveCombatSessionId = state.Value.ActiveCombatSessionId,
            InfilSessionId = state.Value.InfilSessionId,
            InfilStartTime = state.Value.InfilStartTime,
            LockedLoadout = state.Value.LockedLoadout,
            Pet = state.Value.Pet
        };
        var expectedResult = OfflineCombatReplay.ProjectOperatorResult(initialOperator, outcomeResult.Value!);
        var expectedResultHash = OfflineMissionHashing.ComputeOperatorStateHash(expectedResult);

        var envelope = new OfflineMissionEnvelope
        {
            OperatorId = operatorId.ToString(),
            SequenceNumber = 2,
            RandomSeed = sessionSnapshot!.Seed,
            InitialSnapshotJson = System.Text.Json.JsonSerializer.Serialize(initialOperator),
            ResultSnapshotJson = System.Text.Json.JsonSerializer.Serialize(expectedResult),
            InitialCombatSnapshotJson = sessionSnapshot.ReplayInitialSnapshotJson,
            FinalCombatSnapshotHash = OfflineCombatReplay.ComputeCombatSnapshotHash(replayResult.FinalSession),
            InitialOperatorStateHash = initialHash,
            ResultOperatorStateHash = expectedResultHash,
            ReplayTurns = sessionSnapshot.ReplayTurns.ToList(),
            FullBattleLog = sessionResult.Value!.BattleLog,
            ExecutedUtc = DateTime.UtcNow
        };

        var result = await operatorService.SyncOfflineMission(envelope);

        Assert.True(result.IsSuccess, result.ErrorMessage);
    }

    [Fact]
    public async Task SyncOfflineMission_WithTamperedCombatHash_RejectsEnvelope()
    {
        var createResult = await _operatorService.CreateOperatorAsync(new OperatorCreateRequest { Name = "TamperOp" });
        Assert.True(createResult.IsSuccess);
        var operatorId = createResult.Value!.Id;

        Assert.True((await _operatorService.StartInfilAsync(operatorId)).IsSuccess);
        var sessionId = await _operatorService.StartCombatSessionAsync(operatorId);
        Assert.True(sessionId.IsSuccess);

        for (var i = 0; i < 20; i++)
        {
            Assert.True((await _sessionService.SubmitPlayerIntentsAsync(sessionId.Value!, new SubmitIntentsRequest
            {
                OperatorId = operatorId,
                Intents = new IntentDto { Primary = PrimaryAction.Fire }
            })).IsSuccess);

            var advanceResult = await _sessionService.AdvanceAsync(sessionId.Value!, operatorId);
            Assert.True(advanceResult.IsSuccess);
            if (advanceResult.Value!.Phase == SessionPhase.Completed)
                break;
        }

        var operatorState = await _operatorService.GetOperatorAsync(operatorId);
        var sessionSnapshot = await _sessionStore.LoadAsync(sessionId.Value!);
        var outcome = await _sessionService.GetCombatOutcomeAsync(sessionId.Value!);
        var replay = await OfflineCombatReplay.ReplayAsync(sessionSnapshot!.ReplayInitialSnapshotJson, sessionSnapshot.ReplayTurns);

        var initialOperator = new OperatorDto
        {
            Id = operatorId.ToString(),
            Name = operatorState.Value!.Name,
            TotalXp = operatorState.Value.TotalXp,
            CurrentHealth = operatorState.Value.CurrentHealth,
            MaxHealth = operatorState.Value.MaxHealth,
            EquippedWeaponName = operatorState.Value.EquippedWeaponName,
            UnlockedPerks = operatorState.Value.UnlockedPerks,
            ExfilStreak = operatorState.Value.ExfilStreak,
            IsDead = operatorState.Value.IsDead,
            CurrentMode = operatorState.Value.CurrentMode.ToString(),
            ActiveCombatSessionId = operatorState.Value.ActiveCombatSessionId,
            InfilSessionId = operatorState.Value.InfilSessionId,
            InfilStartTime = operatorState.Value.InfilStartTime,
            LockedLoadout = operatorState.Value.LockedLoadout,
            Pet = operatorState.Value.Pet
        };

        var projected = OfflineCombatReplay.ProjectOperatorResult(initialOperator, outcome.Value!);
        var result = await _operatorService.SyncOfflineMission(new OfflineMissionEnvelope
        {
            OperatorId = operatorId.ToString(),
            SequenceNumber = 1,
            RandomSeed = sessionSnapshot.Seed,
            InitialSnapshotJson = System.Text.Json.JsonSerializer.Serialize(initialOperator),
            ResultSnapshotJson = System.Text.Json.JsonSerializer.Serialize(projected),
            InitialCombatSnapshotJson = sessionSnapshot.ReplayInitialSnapshotJson,
            FinalCombatSnapshotHash = "BAD-HASH",
            InitialOperatorStateHash = OfflineMissionHashing.ComputeOperatorStateHash(operatorState.Value!),
            ResultOperatorStateHash = OfflineMissionHashing.ComputeOperatorStateHash(projected),
            ReplayTurns = sessionSnapshot.ReplayTurns.ToList(),
            FullBattleLog = replay.FinalSession.BattleLog,
            ExecutedUtc = DateTime.UtcNow
        });

        Assert.Equal(ResultStatus.InvalidState, result.Status);
        Assert.Contains("combat replay hash mismatch", result.ErrorMessage);
    }

    [Fact]
    public async Task GetOperatorAsync_WithMissingSession_ClearsDanglingReference()
    {
        // Arrange: Create operator with ActiveSessionId but session doesn't exist
        var createResult = await _operatorService.CreateOperatorAsync(new OperatorCreateRequest { Name = "TestOp" });
        var operatorId = createResult.Value!.Id;

        var infilResult = await _operatorService.StartInfilAsync(operatorId);
        
        // Start and create combat session
        var sessionId = await StartAndCreateCombatSessionAsync(operatorId);

        // Delete the session from the store to simulate missing session
        await _sessionStore.DeleteAsync(sessionId);

        // Act: Get operator with dangling ActiveSessionId
        var operatorResult = await _operatorService.GetOperatorAsync(operatorId);

        // Assert: ActiveSessionId should be cleared in the returned DTO
        Assert.True(operatorResult.IsSuccess);
        var op = operatorResult.Value!;
        Assert.Null(op.ActiveCombatSessionId);
        Assert.Null(op.ActiveCombatSession);
    }

    [Fact]
    public async Task CleanupCompletedSessionAsync_WithActiveSession_DoesNotProcess()
    {
        // Arrange: Create operator with active (non-completed) session
        var createResult = await _operatorService.CreateOperatorAsync(new OperatorCreateRequest { Name = "TestOp" });
        var operatorId = createResult.Value!.Id;

        var infilResult = await _operatorService.StartInfilAsync(operatorId);
        
        // Start and create combat session
        var sessionId = await StartAndCreateCombatSessionAsync(operatorId);

        // Act: Cleanup when session is still active (Planning phase)
        var cleanupResult = await _operatorService.CleanupCompletedSessionAsync(operatorId);
        Assert.True(cleanupResult.IsSuccess);

        // Get operator
        var operatorResult = await _operatorService.GetOperatorAsync(operatorId);

        // Assert: Session should NOT be processed (still active)
        Assert.True(operatorResult.IsSuccess);
        var op = operatorResult.Value!;
        Assert.Equal(sessionId, op.ActiveCombatSessionId);
        Assert.NotNull(op.ActiveCombatSession);
        Assert.Equal(SessionPhase.Planning, op.ActiveCombatSession.Phase);
        Assert.Equal(0, op.ExfilStreak); // No streak increment
    }

    [Fact]
    public async Task CleanupCompletedSessionAsync_WhenGetOutcomeFails_ReturnsSuccessWithoutProcessing()
    {
        // Arrange: Create operator with completed session
        var createResult = await _operatorService.CreateOperatorAsync(new OperatorCreateRequest { Name = "TestOp" });
        var operatorId = createResult.Value!.Id;

        var infilResult = await _operatorService.StartInfilAsync(operatorId);
        
        // Start and create combat session
        var sessionId = await StartAndCreateCombatSessionAsync(operatorId);

        // Complete combat
        for (int i = 0; i < 20; i++)
        {
            await _sessionService.SubmitPlayerIntentsAsync(sessionId, new SubmitIntentsRequest
            {
                Intents = new IntentDto { Primary = PrimaryAction.Fire }
            });
            var advanceResult = await _sessionService.AdvanceAsync(sessionId);
            if (advanceResult.Value!.Phase == SessionPhase.Completed)
                break;
        }

        // Delete the session to simulate GetCombatOutcomeAsync failure
        await _sessionStore.DeleteAsync(sessionId);

        // Act: Cleanup should succeed gracefully without processing
        var cleanupResult = await _operatorService.CleanupCompletedSessionAsync(operatorId);
        
        // Assert: Cleanup returns success (non-blocking) even though outcome retrieval failed
        Assert.True(cleanupResult.IsSuccess);

        // Operator state should be unchanged (outcome not processed)
        var operatorResult = await _operatorService.GetOperatorAsync(operatorId);
        Assert.True(operatorResult.IsSuccess);
        
        // ActiveSessionId cleared by GetOperatorAsync due to missing session
        Assert.Null(operatorResult.Value!.ActiveCombatSessionId);
        Assert.Equal(0, operatorResult.Value.ExfilStreak); // No streak increment
    }

    [Fact]
    public async Task CleanupCompletedSessionAsync_WithNoActiveSession_ReturnsSuccessImmediately()
    {
        // Arrange: Create operator in Base mode (no active session)
        var createResult = await _operatorService.CreateOperatorAsync(new OperatorCreateRequest { Name = "TestOp" });
        var operatorId = createResult.Value!.Id;

        // Act: Cleanup when there's no active session
        var cleanupResult = await _operatorService.CleanupCompletedSessionAsync(operatorId);

        // Assert: Should return success immediately without any processing
        Assert.True(cleanupResult.IsSuccess);
    }

    [Fact]
    public async Task CompleteInfilAsync_WithPendingVictoryOutcome_AppliesXpBeforeCompletingInfil()
    {
        // Arrange: Create operator, start infil, run combat to completion
        var createResult = await _operatorService.CreateOperatorAsync(new OperatorCreateRequest { Name = "TestOp" });
        var operatorId = createResult.Value!.Id;
        await _operatorService.StartInfilAsync(operatorId);
        var sessionId = await StartAndCreateCombatSessionAsync(operatorId);

        // Run combat to completion via replay
        for (int i = 0; i < 30; i++)
        {
            await _sessionService.SubmitPlayerIntentsAsync(sessionId, new SubmitIntentsRequest
            {
                Intents = new IntentDto { Primary = PrimaryAction.Fire }
            });
            var state = await _sessionService.AdvanceAsync(sessionId);
            if (state.Value!.Phase == SessionPhase.Completed)
                break;
        }

        var finalSession = await _sessionService.GetStateAsync(sessionId);
        bool playerAlive = finalSession.Value!.Player.IsAlive;
        bool enemyAlive = finalSession.Value!.Enemy.IsAlive;
        bool isVictory = playerAlive && !enemyAlive;

        // Compute the expected XP that ProcessCombatOutcomeAsync will award based on the outcome.
        // This mirrors the XP logic in CombatSession.GetOutcome(): 100 for victory, 50 for
        // surviving without winning, 0 for death.
        int expectedXp = isVictory ? 100 : (playerAlive ? 50 : 0);

        // Verify combat session is completed but outcome is NOT yet processed (XP still 0)
        var beforeExfil = await _operatorService.GetOperatorAsync(operatorId);
        Assert.Equal(OperatorMode.Infil, beforeExfil.Value!.CurrentMode);
        Assert.Equal(0, beforeExfil.Value.TotalXp);
        Assert.NotNull(beforeExfil.Value.ActiveCombatSessionId); // Still has reference

        // Act: Directly call CompleteInfilAsync WITHOUT explicitly calling ProcessCombatOutcomeAsync.
        // The server should auto-process the pending combat outcome via CleanupCompletedSessionAsync.
        var exfilResult = await _operatorService.CompleteInfilAsync(operatorId);
        Assert.True(exfilResult.IsSuccess, exfilResult.ErrorMessage ?? "CompleteInfil failed");

        // Assert: Operator is in Base mode regardless of victory or defeat
        var afterExfil = await _operatorService.GetOperatorAsync(operatorId);
        var afterExfilOp = afterExfil.Value!;
        Assert.Equal(OperatorMode.Base, afterExfilOp.CurrentMode);
        Assert.Null(afterExfilOp.ActiveCombatSessionId);

        // XP from the replay-derived outcome was applied before infil completed
        Assert.Equal(expectedXp, afterExfilOp.TotalXp);

        // Streak is incremented only when the full infil completes successfully (not on death)
        Assert.Equal(isVictory ? 1 : 0, afterExfilOp.ExfilStreak);
    }

    [Fact]
    public async Task CompleteInfilAsync_WithPendingDeathOutcome_TransitionsToBaseWithoutStreakIncrement()
    {
        // Arrange: Create a deterministic "player died" scenario by saving a crafted completed
        // snapshot directly to the store with Player.Health = 0 and FinalHash = null (which skips
        // hash validation). This avoids relying on combat RNG to produce a death outcome.
        var createResult = await _operatorService.CreateOperatorAsync(new OperatorCreateRequest { Name = "TestOp" });
        var operatorId = createResult.Value!.Id;
        await _operatorService.StartInfilAsync(operatorId);

        // Reserve a fixed session ID and register it on the operator aggregate
        var sessionId = Guid.NewGuid();
        var startResult = await _exfilService.StartCombatSessionAsync(new OperatorId(operatorId), sessionId);
        Assert.True(startResult.IsSuccess, "Failed to register combat session on operator aggregate");

        // Save a completed snapshot where the player is dead (Health = 0) and the enemy is alive.
        // FinalHash = null and ReplayInitialSnapshotJson = "" instruct the store's validation to
        // skip hash checks, keeping the test focused on the operator-level behavior under test
        // (auto-processing of a death outcome in CompleteInfilAsync). Session-store hash
        // validation is independently tested in LiteDbCombatSessionStoreTests and
        // CombatSessionReplayIntegrityTests.
        var deadPlayerSnapshot = new CombatSessionSnapshot
        {
            Id = sessionId,
            OperatorId = operatorId,
            Phase = SessionPhase.Completed,
            TurnNumber = 1,
            Seed = 42,
            CreatedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow,
            ReplayInitialSnapshotJson = string.Empty, // empty → skip replay hash check
            FinalHash = null,                          // null → skip state-based hash check
            Player = new OperatorSnapshot
            {
                Id = Guid.NewGuid(),
                Name = "TestOp",
                Health = 0f,       // dead
                MaxHealth = 100f,
                Accuracy = 0.5f
            },
            Enemy = new OperatorSnapshot
            {
                Id = Guid.NewGuid(),
                Name = "Enemy",
                Health = 100f,     // alive
                MaxHealth = 100f,
                Accuracy = 0.5f
            },
            Combat = new CombatStateSnapshot
            {
                Phase = CombatPhase.Ended,
                RandomState = new RandomStateSnapshot { Seed = 42 }
            },
            Pet = new PetStateSnapshot
            {
                OperatorId = operatorId,
                Health = 100f,
                Morale = 100f,
                Hydration = 100f,
                LastUpdated = DateTimeOffset.UtcNow
            }
        };
        await _sessionStore.SaveAsync(deadPlayerSnapshot);

        // Verify the setup: operator is in Infil with an active session pointing to our snapshot.
        var beforeExfil = await _operatorService.GetOperatorAsync(operatorId);
        Assert.Equal(OperatorMode.Infil, beforeExfil.Value!.CurrentMode);
        Assert.Equal(sessionId, beforeExfil.Value.ActiveCombatSessionId);

        // Act: Exfil without explicit outcome processing — CompleteInfilAsync should auto-process
        // the pending death outcome via CleanupCompletedSessionAsync, emit OperatorDied +
        // InfilEnded events, and then return early (operator is already in Base mode).
        var exfilResult = await _operatorService.CompleteInfilAsync(operatorId);
        Assert.True(exfilResult.IsSuccess, exfilResult.ErrorMessage ?? "CompleteInfil failed");

        // Assert: Operator is in Base mode (death → InfilEnded was already emitted by cleanup),
        // no XP (death awards 0 XP), streak reset to 0.
        var afterExfil = await _operatorService.GetOperatorAsync(operatorId);
        var afterExfilOp = afterExfil.Value!;
        Assert.Equal(OperatorMode.Base, afterExfilOp.CurrentMode);
        Assert.Equal(0, afterExfilOp.TotalXp);         // no XP for death
        Assert.Equal(0, afterExfilOp.ExfilStreak);      // streak reset on death/failed infil
        Assert.Null(afterExfilOp.ActiveCombatSessionId);
    }

    [Fact]
    public async Task CompleteInfilAsync_WithNoActiveCombatSession_CompletesNormally()
    {
        // Arrange: Create operator in Infil mode with no active combat session
        var createResult = await _operatorService.CreateOperatorAsync(new OperatorCreateRequest { Name = "TestOp" });
        var operatorId = createResult.Value!.Id;
        await _operatorService.StartInfilAsync(operatorId);

        // No combat session started — operator is in Infil with no ActiveCombatSessionId

        // Act: CompleteInfilAsync should succeed without any cleanup needed
        var exfilResult = await _operatorService.CompleteInfilAsync(operatorId);
        Assert.True(exfilResult.IsSuccess, exfilResult.ErrorMessage ?? "CompleteInfil failed");

        // Assert: Operator is in Base mode, streak incremented
        var afterExfil = await _operatorService.GetOperatorAsync(operatorId);
        var afterExfilOp = afterExfil.Value!;
        Assert.Equal(OperatorMode.Base, afterExfilOp.CurrentMode);
        Assert.Equal(1, afterExfilOp.ExfilStreak);
    }

    [Fact]
    public async Task CompleteInfilAsync_WhenOutcomeAlreadyProcessed_DoesNotDoubleApplyXp()
    {
        // Arrange: Create operator, start infil, complete combat, and process outcome explicitly
        var createResult = await _operatorService.CreateOperatorAsync(new OperatorCreateRequest { Name = "TestOp" });
        var operatorId = createResult.Value!.Id;
        await _operatorService.StartInfilAsync(operatorId);
        var sessionId = await StartAndCreateCombatSessionAsync(operatorId);

        // Run combat to completion
        for (int i = 0; i < 30; i++)
        {
            await _sessionService.SubmitPlayerIntentsAsync(sessionId, new SubmitIntentsRequest
            {
                Intents = new IntentDto { Primary = PrimaryAction.Fire }
            });
            var state = await _sessionService.AdvanceAsync(sessionId);
            if (state.Value!.Phase == SessionPhase.Completed)
                break;
        }

        // Explicitly process the outcome (simulates ConsoleClient calling /infil/outcome)
        await _operatorService.CleanupCompletedSessionAsync(operatorId);

        // Capture XP after first processing
        var afterFirstProcess = await _operatorService.GetOperatorAsync(operatorId);
        var xpAfterCombat = afterFirstProcess.Value!.TotalXp;

        // Act: Exfil — this triggers CleanupCompletedSessionAsync again but should be a no-op
        var exfilResult = await _operatorService.CompleteInfilAsync(operatorId);
        Assert.True(exfilResult.IsSuccess, exfilResult.ErrorMessage ?? "CompleteInfil failed");

        // Assert: XP is unchanged (no double-application)
        var afterExfil = await _operatorService.GetOperatorAsync(operatorId);
        var afterExfilOp = afterExfil.Value!;
        Assert.Equal(xpAfterCombat, afterExfilOp.TotalXp);
        Assert.Equal(OperatorMode.Base, afterExfilOp.CurrentMode);
    }

    [Fact]
    public async Task StartCombatSessionAsync_WithDanglingReference_ClearsAndStartsNew()
    {
        // Regression test: reproduces the "Failed to start next combat" bug.
        // The scenario: a combat session is deleted from the store (e.g., by StartNextCombatInInfil)
        // without a CombatVictoryEvent being emitted, leaving ActiveCombatSessionId set in the
        // aggregate. The next call to StartCombatSessionAsync must detect this and recover
        // automatically instead of returning "Cannot start new combat session while one is already active".

        // Arrange: Create operator, start infil, start and then delete a combat session
        var createResult = await _operatorService.CreateOperatorAsync(new OperatorCreateRequest { Name = "TestOp" });
        Assert.True(createResult.IsSuccess);
        var operatorId = createResult.Value!.Id;

        await _operatorService.StartInfilAsync(operatorId);

        // Simulate the two-step StartNewCombatSession flow: emit event + create session
        var staleSessionId = await StartAndCreateCombatSessionAsync(operatorId);

        // Simulate session deletion without CombatVictoryEvent (the dangling reference scenario)
        await _sessionStore.DeleteAsync(staleSessionId);

        // Verify the aggregate still has the stale reference
        var beforeRepair = await _exfilService.LoadOperatorAsync(new OperatorId(operatorId));
        Assert.Equal(staleSessionId, beforeRepair.Value!.ActiveCombatSessionId);

        // Act: StartCombatSessionAsync should detect the dangling reference, clear it, and succeed
        var startResult = await _operatorService.StartCombatSessionAsync(operatorId);

        // Assert: A new session ID is returned and the operator's reference is updated
        Assert.True(startResult.IsSuccess, $"Expected success but got: {startResult.ErrorMessage}");
        var newSessionId = startResult.Value!;
        Assert.NotEqual(staleSessionId, newSessionId);

        // Verify aggregate was updated
        var afterRepair = await _exfilService.LoadOperatorAsync(new OperatorId(operatorId));
        Assert.Equal(newSessionId, afterRepair.Value!.ActiveCombatSessionId);
    }

    [Fact]
    public async Task StartCombatSessionAsync_WithExistingActiveSession_ReturnsExistingSession()
    {
        var createResult = await _operatorService.CreateOperatorAsync(new OperatorCreateRequest { Name = "ResumeOp" });
        Assert.True(createResult.IsSuccess);
        var operatorId = createResult.Value!.Id;

        var infilResult = await _operatorService.StartInfilAsync(operatorId);
        Assert.True(infilResult.IsSuccess);

        var sessionId = await StartAndCreateCombatSessionAsync(operatorId);

        var secondStartResult = await _operatorService.StartCombatSessionAsync(operatorId);

        Assert.True(secondStartResult.IsSuccess);
        Assert.Equal(sessionId, secondStartResult.Value);

        var operatorAfterSecondStart = await _operatorService.GetOperatorAsync(operatorId);
        Assert.True(operatorAfterSecondStart.IsSuccess);
        Assert.Equal(sessionId, operatorAfterSecondStart.Value!.ActiveCombatSessionId);
        Assert.NotNull(operatorAfterSecondStart.Value.ActiveCombatSession);
        Assert.Equal(SessionPhase.Planning, operatorAfterSecondStart.Value.ActiveCombatSession!.Phase);
    }

    [Fact]
    public async Task DeleteSessionAsync_PreservesCombatSessionAuditRecord()
    {
        var createResult = await _operatorService.CreateOperatorAsync(new OperatorCreateRequest { Name = "AuditOp" });
        Assert.True(createResult.IsSuccess);
        var operatorId = createResult.Value!.Id;

        var infilResult = await _operatorService.StartInfilAsync(operatorId);
        Assert.True(infilResult.IsSuccess);

        var sessionId = await StartAndCreateCombatSessionAsync(operatorId);

        var deleteResult = await _sessionService.DeleteSessionAsync(sessionId);

        Assert.Equal(ResultStatus.InvalidState, deleteResult.Status);

        var sessionState = await _sessionService.GetStateAsync(sessionId);
        Assert.True(sessionState.IsSuccess);
        Assert.Equal(sessionId, sessionState.Value!.Id);
    }

    [Fact]
    public async Task ProcessCombatOutcomeAsync_WithEmptySessionId_ReturnsValidationError()
    {
        var createResult = await _operatorService.CreateOperatorAsync(new OperatorCreateRequest { Name = "TestOp" });
        Assert.True(createResult.IsSuccess);
        var operatorId = createResult.Value!.Id;

        var result = await _operatorService.ProcessCombatOutcomeAsync(operatorId, new ProcessOutcomeRequest
        {
            SessionId = Guid.Empty
        });

        Assert.Equal(ResultStatus.ValidationError, result.Status);
    }

    [Fact]
    public async Task ProcessCombatOutcomeAsync_WithNoActiveSession_ReturnsInvalidState()
    {
        var createResult = await _operatorService.CreateOperatorAsync(new OperatorCreateRequest { Name = "TestOp" });
        Assert.True(createResult.IsSuccess);
        var operatorId = createResult.Value!.Id;

        var result = await _operatorService.ProcessCombatOutcomeAsync(operatorId, new ProcessOutcomeRequest
        {
            SessionId = Guid.NewGuid()
        });

        Assert.Equal(ResultStatus.InvalidState, result.Status);
    }

    [Fact]
    public async Task ProcessCombatOutcomeAsync_WithMismatchedSessionId_ReturnsInvalidState()
    {
        var createResult = await _operatorService.CreateOperatorAsync(new OperatorCreateRequest { Name = "TestOp" });
        Assert.True(createResult.IsSuccess);
        var operatorId = createResult.Value!.Id;

        var infilResult = await _operatorService.StartInfilAsync(operatorId);
        Assert.True(infilResult.IsSuccess);

        await StartAndCreateCombatSessionAsync(operatorId);

        var result = await _operatorService.ProcessCombatOutcomeAsync(operatorId, new ProcessOutcomeRequest
        {
            SessionId = Guid.NewGuid()
        });

        Assert.Equal(ResultStatus.InvalidState, result.Status);
    }

    [Fact]
    public async Task GetOperatorAsync_WhenInfilTimerExpired_AutoFailsInfilAndReturnsBaseMode()
    {
        // Arrange: use an in-memory event store so we can back-date the InfilStartedEvent
        var expiredEventStore = new InMemoryOperatorEventStore();
        var expiredExfilService = new OperatorExfilService(expiredEventStore);
        var sessionStore = new LiteDbCombatSessionStore(_database);
        var sessionService = new CombatSessionService(sessionStore, expiredEventStore);
        var svc = new OperatorService(expiredExfilService, sessionService, expiredEventStore);

        var createResult = await expiredExfilService.CreateOperatorAsync("TimerExpiredOp");
        Assert.True(createResult.IsSuccess);
        var operatorId = createResult.Value!;

        // Directly append a backdated InfilStartedEvent (31 minutes ago)
        var existingEvents = await expiredEventStore.LoadEventsAsync(operatorId);
        var lastEvent = existingEvents[^1];
        var expiredInfilEvent = new InfilStartedEvent(
            operatorId,
            sequenceNumber: lastEvent.SequenceNumber + 1,
            sessionId: Guid.NewGuid(),
            lockedLoadout: "SOKOL 545",
            infilStartTime: DateTimeOffset.UtcNow.AddMinutes(ExpiredInfilMinutes),
            previousHash: lastEvent.Hash);
        await expiredEventStore.AppendEventAsync(expiredInfilEvent);

        // Pre-condition: operator is in Infil mode
        var before = await expiredExfilService.LoadOperatorAsync(operatorId);
        Assert.Equal(OperatorMode.Infil, before.Value!.CurrentMode);

        // Act: GET the operator — server should auto-fail the expired infil
        var dto = await svc.GetOperatorAsync(operatorId.Value);

        // Assert: returned state is Base mode
        Assert.True(dto.IsSuccess);
        Assert.Equal(OperatorMode.Base, dto.Value!.CurrentMode);
        Assert.Null(dto.Value!.InfilStartTime);
    }

    [Fact]
    public async Task GetOperatorAsync_WhenInfilTimerExpired_ThenChangeLoadoutSucceeds()
    {
        // Arrange: operator with an expired infil
        var expiredEventStore = new InMemoryOperatorEventStore();
        var expiredExfilService = new OperatorExfilService(expiredEventStore);
        var sessionStore = new LiteDbCombatSessionStore(_database);
        var sessionService = new CombatSessionService(sessionStore, expiredEventStore);
        var svc = new OperatorService(expiredExfilService, sessionService, expiredEventStore);

        var createResult = await expiredExfilService.CreateOperatorAsync("LoadoutTestOp");
        Assert.True(createResult.IsSuccess);
        var operatorId = createResult.Value!;

        var existingEvents = await expiredEventStore.LoadEventsAsync(operatorId);
        var lastEvent = existingEvents[^1];
        var expiredInfilEvent = new InfilStartedEvent(
            operatorId,
            sequenceNumber: lastEvent.SequenceNumber + 1,
            sessionId: Guid.NewGuid(),
            lockedLoadout: "SOKOL 545",
            infilStartTime: DateTimeOffset.UtcNow.AddMinutes(ExpiredInfilMinutes),
            previousHash: lastEvent.Hash);
        await expiredEventStore.AppendEventAsync(expiredInfilEvent);

        // Act: GET auto-fails the infil; then loadout change should work
        var getResult = await svc.GetOperatorAsync(operatorId.Value);
        Assert.True(getResult.IsSuccess);
        Assert.Equal(OperatorMode.Base, getResult.Value!.CurrentMode);

        var loadoutResult = await svc.ChangeLoadoutAsync(operatorId.Value, new ChangeLoadoutRequest { WeaponName = "STURMWOLF 45" });
        Assert.True(loadoutResult.IsSuccess);
    }

    [Fact]
    public async Task GetOperatorAsync_WhenInfilNotExpired_DoesNotAutoFail()
    {
        // Arrange: operator with a fresh (non-expired) infil
        var createResult = await _operatorService.CreateOperatorAsync(new OperatorCreateRequest { Name = "FreshInfilOp" });
        Assert.True(createResult.IsSuccess);
        var operatorId = createResult.Value!.Id;

        var infilResult = await _operatorService.StartInfilAsync(operatorId);
        Assert.True(infilResult.IsSuccess);

        // Act: GET should NOT auto-fail a non-expired infil
        var dto = await _operatorService.GetOperatorAsync(operatorId);

        Assert.True(dto.IsSuccess);
        Assert.Equal(OperatorMode.Infil, dto.Value!.CurrentMode);
        Assert.NotNull(dto.Value!.InfilStartTime);
    }

    [Fact]
    public async Task GetOperatorAsync_WhenInfilTimerExpired_ResetsExfilStreak()
    {
        // Validates that the auto-fail triggered by GetOperatorAsync applies the streak reset
        // penalty (same as a regular ExfilFailed), not just the mode transition.

        // Arrange: operator with a backdated (expired) infil
        var expiredEventStore = new InMemoryOperatorEventStore();
        var expiredExfilService = new OperatorExfilService(expiredEventStore);
        var sessionStore = new LiteDbCombatSessionStore(_database);
        var sessionService = new CombatSessionService(sessionStore, expiredEventStore);
        var svc = new OperatorService(expiredExfilService, sessionService, expiredEventStore);

        var createResult = await expiredExfilService.CreateOperatorAsync("StreakTestOp");
        Assert.True(createResult.IsSuccess);
        var operatorId = createResult.Value!;

        var existingEvents = await expiredEventStore.LoadEventsAsync(operatorId);
        var lastEvent = existingEvents[^1];
        var expiredInfilEvent = new InfilStartedEvent(
            operatorId,
            sequenceNumber: lastEvent.SequenceNumber + 1,
            sessionId: Guid.NewGuid(),
            lockedLoadout: "SOKOL 545",
            infilStartTime: DateTimeOffset.UtcNow.AddMinutes(ExpiredInfilMinutes),
            previousHash: lastEvent.Hash);
        await expiredEventStore.AppendEventAsync(expiredInfilEvent);

        // Act: GET the operator — server should auto-fail the expired infil
        var dto = await svc.GetOperatorAsync(operatorId.Value);

        // Assert: ExfilStreak is reset to 0 (penalty for losing the infil to the timer)
        Assert.True(dto.IsSuccess);
        Assert.Equal(OperatorMode.Base, dto.Value!.CurrentMode);
        Assert.Equal(0, dto.Value!.ExfilStreak);
    }

    [Fact]
    public async Task RetreatFromCombatAsync_WithActiveSession_ClearsSessionAndRemainsInInfil()
    {
        // Arrange: create operator, start infil, start combat
        var createResult = await _operatorService.CreateOperatorAsync(new OperatorCreateRequest { Name = "RetreatOp" });
        Assert.True(createResult.IsSuccess);
        var operatorId = createResult.Value!.Id;

        var infilResult = await _operatorService.StartInfilAsync(operatorId);
        Assert.True(infilResult.IsSuccess);

        var sessionId = await StartAndCreateCombatSessionAsync(operatorId);

        // Pre-condition: operator is in Infil mode with an active session
        var before = await _operatorService.GetOperatorAsync(operatorId);
        Assert.True(before.IsSuccess);
        Assert.Equal(OperatorMode.Infil, before.Value!.CurrentMode);
        Assert.Equal(sessionId, before.Value.ActiveCombatSessionId);

        // Act: retreat from combat
        var retreatResult = await _operatorService.RetreatFromCombatAsync(operatorId);
        Assert.True(retreatResult.IsSuccess);

        // Assert: operator is still in Infil mode (not kicked back to Base)
        var after = await _operatorService.GetOperatorAsync(operatorId);
        Assert.True(after.IsSuccess);
        Assert.Equal(OperatorMode.Infil, after.Value!.CurrentMode);

        // ActiveCombatSessionId cleared
        Assert.Null(after.Value.ActiveCombatSessionId);
        Assert.Null(after.Value.ActiveCombatSession);

        // ExfilStreak unchanged (retreat is not an exfil failure)
        Assert.Equal(0, after.Value.ExfilStreak);

        // The session record itself is preserved in the store
        var sessionState = await _sessionService.GetStateAsync(sessionId);
        Assert.True(sessionState.IsSuccess);
    }

    [Fact]
    public async Task RetreatFromCombatAsync_WithNoActiveSession_ReturnsSuccess()
    {
        // Arrange: operator in Infil mode but no active combat session
        var createResult = await _operatorService.CreateOperatorAsync(new OperatorCreateRequest { Name = "NoSessionOp" });
        Assert.True(createResult.IsSuccess);
        var operatorId = createResult.Value!.Id;

        await _operatorService.StartInfilAsync(operatorId);

        // Act: retreat when there is no active combat session (no-op)
        var retreatResult = await _operatorService.RetreatFromCombatAsync(operatorId);

        // Assert: idempotent success — nothing to clear
        Assert.True(retreatResult.IsSuccess);

        var after = await _operatorService.GetOperatorAsync(operatorId);
        Assert.True(after.IsSuccess);
        Assert.Equal(OperatorMode.Infil, after.Value!.CurrentMode);
        Assert.Null(after.Value.ActiveCombatSessionId);
    }

    [Fact]
    public async Task StartCombatSessionAsync_WhenCleanupWriteFails_PropagatesError()
    {
        // Regression test for the fix that surfaces ClearDanglingCombatSessionAsync failures.
        // Scenario: operator has a dangling ActiveCombatSessionId (session deleted without
        // emitting CombatVictoryEvent). When the event-store write that clears the reference
        // throws, StartCombatSessionAsync must propagate that error instead of continuing.

        // Arrange: set up an operator with a dangling session reference using a store that
        // we can make fail on demand.
        var failingStore = new FailingAppendOnTriggerStore();
        var exfilService = new OperatorExfilService(failingStore);
        var sessionStore = new LiteDbCombatSessionStore(_database);
        var sessionService = new CombatSessionService(sessionStore, failingStore);
        var svc = new OperatorService(exfilService, sessionService, failingStore);

        // Create operator and start infil using the normal (non-failing) store path
        var createResult = await exfilService.CreateOperatorAsync("CleanupFailOp");
        Assert.True(createResult.IsSuccess);
        var operatorId = createResult.Value!;

        var infilResult = await exfilService.StartInfilAsync(operatorId);
        Assert.True(infilResult.IsSuccess);

        // Emit a CombatSessionStartedEvent so the operator has an ActiveCombatSessionId
        var staleSessionId = Guid.NewGuid();
        var staleResult = await exfilService.StartCombatSessionAsync(operatorId, staleSessionId);
        Assert.True(staleResult.IsSuccess);

        // Do NOT create the session in the store — this creates the dangling reference.
        // Verify the dangling state.
        var beforeRepair = await exfilService.LoadOperatorAsync(operatorId);
        Assert.Equal(staleSessionId, beforeRepair.Value!.ActiveCombatSessionId);

        // Enable the write failure on the store so the ClearDanglingCombatSessionAsync
        // event-store append throws.
        failingStore.EnableWriteFailure("Simulated event store write failure");

        // Act
        var startResult = await svc.StartCombatSessionAsync(operatorId.Value);

        // Assert: the error from the cleanup write must be surfaced to the caller
        Assert.False(startResult.IsSuccess);
        Assert.Contains("Simulated event store write failure", startResult.ErrorMessage);

        // The operator's dangling reference must still be set (not silently cleared)
        var afterFailure = await exfilService.LoadOperatorAsync(operatorId);
        Assert.Equal(staleSessionId, afterFailure.Value!.ActiveCombatSessionId);
    }

    /// <summary>
    /// An event store that wraps InMemoryOperatorEventStore and can be told to fail all
    /// subsequent writes. Used to test that write errors are correctly propagated.
    /// </summary>
    private sealed class FailingAppendOnTriggerStore : IOperatorEventStore
    {
        private readonly InMemoryOperatorEventStore _inner = new();
        private string? _failureMessage;

        /// <summary>After calling this, every AppendEventAsync/AppendEventsAsync call throws.</summary>
        public void EnableWriteFailure(string message) => _failureMessage = message;

        public Task<IReadOnlyList<OperatorEvent>> LoadEventsAsync(OperatorId operatorId)
            => _inner.LoadEventsAsync(operatorId);

        public Task AppendEventAsync(OperatorEvent evt)
        {
            if (_failureMessage is not null)
                throw new InvalidOperationException(_failureMessage);
            return _inner.AppendEventAsync(evt);
        }

        public Task AppendEventsAsync(IReadOnlyList<OperatorEvent> events)
        {
            if (_failureMessage is not null)
                throw new InvalidOperationException(_failureMessage);
            return _inner.AppendEventsAsync(events);
        }

        public Task<bool> OperatorExistsAsync(OperatorId operatorId)
            => _inner.OperatorExistsAsync(operatorId);

        public Task<long> GetCurrentSequenceAsync(OperatorId operatorId)
            => _inner.GetCurrentSequenceAsync(operatorId);

        public Task<IReadOnlyList<OperatorId>> ListOperatorIdsAsync()
            => _inner.ListOperatorIdsAsync();

        public Task<IReadOnlyList<OperatorId>> ListOperatorIdsByAccountAsync(Guid accountId)
            => _inner.ListOperatorIdsByAccountAsync(accountId);

        public Task<Guid?> GetOperatorAccountIdAsync(OperatorId operatorId)
            => _inner.GetOperatorAccountIdAsync(operatorId);

        public Task AssociateOperatorWithAccountAsync(OperatorId operatorId, Guid accountId)
            => _inner.AssociateOperatorWithAccountAsync(operatorId, accountId);
    }
}
