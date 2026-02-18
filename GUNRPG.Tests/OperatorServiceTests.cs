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

    [Fact]
    public async Task GetOperatorAsync_WithCompletedSession_AutoProcessesOutcome()
    {
        // Arrange: Create operator and start infil
        var createResult = await _operatorService.CreateOperatorAsync(new OperatorCreateRequest { Name = "TestOp" });
        Assert.True(createResult.IsSuccess);
        var operatorId = createResult.Value!.Id;

        var infilResult = await _operatorService.StartInfilAsync(operatorId);
        Assert.True(infilResult.IsSuccess);
        var sessionId = infilResult.Value!.SessionId;

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

        // Act: Load operator (should auto-process the completed outcome)
        var operatorResult = await _operatorService.GetOperatorAsync(operatorId);

        // Assert: Outcome should be processed
        Assert.True(operatorResult.IsSuccess);
        var op = operatorResult.Value!;

        // ActiveSessionId should be cleared after outcome processing
        Assert.Null(op.ActiveSessionId);
        
        // Operator should still be in Infil mode (victory keeps you in infil)
        Assert.Equal(OperatorMode.Infil, op.CurrentMode);
        
        // Exfil streak should be incremented (victory outcome processed)
        Assert.Equal(1, op.ExfilStreak);
        
        // ActiveCombatSession should be null (outcome processed, session reference cleared)
        Assert.Null(op.ActiveCombatSession);
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
        var sessionId = infilResult.Value!.SessionId;

        // Act: Load operator with active (non-completed) session
        var operatorResult = await _operatorService.GetOperatorAsync(operatorId);

        // Assert
        Assert.True(operatorResult.IsSuccess);
        var op = operatorResult.Value!;

        // ActiveSessionId should be set
        Assert.Equal(sessionId, op.ActiveSessionId);
        
        // ActiveCombatSession should be included
        Assert.NotNull(op.ActiveCombatSession);
        Assert.Equal(sessionId, op.ActiveCombatSession.Id);
        
        // Session should be in Planning phase (not completed)
        Assert.Equal(SessionPhase.Planning, op.ActiveCombatSession.Phase);
    }

    [Fact]
    public async Task GetOperatorAsync_AfterAutoProcessedOutcome_OperatorCanCompleteExfil()
    {
        // Arrange: Create operator, start infil, and complete combat
        var createResult = await _operatorService.CreateOperatorAsync(new OperatorCreateRequest { Name = "TestOp" });
        var operatorId = createResult.Value!.Id;

        var infilResult = await _operatorService.StartInfilAsync(operatorId);
        var sessionId = infilResult.Value!.SessionId;

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

        // Load operator (auto-processes outcome)
        var operatorAfterOutcome = await _operatorService.GetOperatorAsync(operatorId);

        // Assert: After victory, operator stays in Infil mode but with no active session
        Assert.Equal(OperatorMode.Infil, operatorAfterOutcome.Value!.CurrentMode);
        Assert.Null(operatorAfterOutcome.Value.ActiveSessionId);
        Assert.Equal(1, operatorAfterOutcome.Value.ExfilStreak);

        // Act: CompleteExfil should work (this is called when user explicitly exfils after victory)
        var exfilResult = await _exfilService.CompleteExfilAsync(new OperatorId(operatorId));

        // Assert: Exfil completes successfully
        // Note: Per design, operator STAYS in Infil mode after successful exfil.
        // They only return to Base after death or failed exfil.
        Assert.True(exfilResult.IsSuccess);

        var finalState = await _operatorService.GetOperatorAsync(operatorId);
        Assert.Equal(OperatorMode.Infil, finalState.Value!.CurrentMode);  // Still in Infil!
        Assert.Null(finalState.Value.ActiveSessionId);
        Assert.Equal(2, finalState.Value.ExfilStreak); // Streak incremented again
    }
}
