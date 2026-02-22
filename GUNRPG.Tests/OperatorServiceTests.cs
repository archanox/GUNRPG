using GUNRPG.Application.Dtos;
using GUNRPG.Application.Operators;
using GUNRPG.Application.Requests;
using GUNRPG.Application.Services;
using GUNRPG.Application.Sessions;
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
    /// Helper method to start a combat session and create the actual session object.
    /// This is the standard two-step process: emit event, then create session.
    /// </summary>
    private async Task<Guid> StartAndCreateCombatSessionAsync(Guid operatorId, string playerName)
    {
        // Start a combat session (emits event)
        var startCombatResult = await _operatorService.StartCombatSessionAsync(operatorId);
        if (!startCombatResult.IsSuccess)
            throw new InvalidOperationException($"Failed to start combat session: {startCombatResult.ErrorMessage}");
        
        var sessionId = startCombatResult.Value!;
        
        // Create the actual combat session
        var sessionRequest = new SessionCreateRequest
        {
            Id = sessionId,
            OperatorId = operatorId,
            PlayerName = playerName
        };
        var sessionCreateResult = await _sessionService.CreateSessionAsync(sessionRequest);
        if (!sessionCreateResult.IsSuccess)
            throw new InvalidOperationException($"Failed to create session: {sessionCreateResult.ErrorMessage}");
        
        return sessionId;
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
        var sessionId = await StartAndCreateCombatSessionAsync(operatorId, "TestOp");

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
        var sessionId = await StartAndCreateCombatSessionAsync(operatorId, "TestOp");

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
        var sessionId = await StartAndCreateCombatSessionAsync(operatorId, "TestOp");

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
        var sessionId = await StartAndCreateCombatSessionAsync(operatorId, "TestOp");

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
    public async Task GetOperatorAsync_WithMissingSession_ClearsDanglingReference()
    {
        // Arrange: Create operator with ActiveSessionId but session doesn't exist
        var createResult = await _operatorService.CreateOperatorAsync(new OperatorCreateRequest { Name = "TestOp" });
        var operatorId = createResult.Value!.Id;

        var infilResult = await _operatorService.StartInfilAsync(operatorId);
        
        // Start and create combat session
        var sessionId = await StartAndCreateCombatSessionAsync(operatorId, "TestOp");

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
        var sessionId = await StartAndCreateCombatSessionAsync(operatorId, "TestOp");

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
        var sessionId = await StartAndCreateCombatSessionAsync(operatorId, "TestOp");

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
        var staleSessionId = await StartAndCreateCombatSessionAsync(operatorId, "TestOp");

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
}
