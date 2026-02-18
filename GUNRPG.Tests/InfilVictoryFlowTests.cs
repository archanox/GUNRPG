using GUNRPG.Application.Combat;
using GUNRPG.Application.Operators;
using GUNRPG.Application.Services;
using GUNRPG.Application.Sessions;
using GUNRPG.Core.Equipment;
using GUNRPG.Core.Operators;
using GUNRPG.Tests.Stubs;
using Xunit;

namespace GUNRPG.Tests;

public class InfilVictoryFlowTests
{
    private readonly OperatorExfilService _service;

    public InfilVictoryFlowTests()
    {
        var eventStore = new InMemoryOperatorEventStore();
        _service = new OperatorExfilService(eventStore);
    }

    [Fact]
    public async Task AfterVictory_OperatorCanStartNewCombat()
    {
        // This test validates the fix for the issue where operators
        // couldn't engage in consecutive battles after a victory.
        // It properly tests the StartCombatSessionAsync method and CombatSessionStartedEvent flow.
        
        // Arrange: Create operator and start infil
        var createResult = await _service.CreateOperatorAsync("TestOp");
        var operatorId = createResult.Value!;

        var infilResult = await _service.StartInfilAsync(operatorId);
        Assert.True(infilResult.IsSuccess);
        var session1 = infilResult.Value;

        // Act 1: Win first combat
        var victory1 = new CombatOutcome(
            sessionId: session1,
            operatorId: operatorId,
            operatorDied: false,
            damageTaken: 20f,
            xpGained: 100,
            gearLost: Array.Empty<GearId>(),
            isVictory: true,
            completedAt: DateTimeOffset.UtcNow);

        var processResult = await _service.ProcessCombatOutcomeAsync(victory1, playerConfirmed: true);
        Assert.True(processResult.IsSuccess);

        // Assert 1: Operator should be in Infil mode but with no active combat session
        var load1 = await _service.LoadOperatorAsync(operatorId);
        Assert.Equal(OperatorMode.Infil, load1.Value!.CurrentMode);
        Assert.Null(load1.Value!.ActiveCombatSessionId); // Session cleared after victory
        Assert.NotNull(load1.Value!.InfilSessionId); // Infil session persists
        Assert.Equal(1, load1.Value!.ExfilStreak);

        // Act 2: Start second combat using the proper StartCombatSessionAsync method
        // This emits CombatSessionStartedEvent and updates ActiveCombatSessionId
        var startCombatResult = await _service.StartCombatSessionAsync(operatorId);
        Assert.True(startCombatResult.IsSuccess);
        var session2 = startCombatResult.Value!;

        // Verify CombatSessionStartedEvent was emitted and ActiveCombatSessionId is set
        var load1b = await _service.LoadOperatorAsync(operatorId);
        Assert.Equal(session2, load1b.Value!.ActiveCombatSessionId);
        Assert.NotNull(load1b.Value!.InfilSessionId); // Infil session still persists

        // Act 3: Win second combat
        var victory2 = new CombatOutcome(
            sessionId: session2,
            operatorId: operatorId,
            operatorDied: false,
            damageTaken: 15f,
            xpGained: 150,
            gearLost: Array.Empty<GearId>(),
            isVictory: true,
            completedAt: DateTimeOffset.UtcNow);

        var processResult2 = await _service.ProcessCombatOutcomeAsync(victory2, playerConfirmed: true);
        Assert.True(processResult2.IsSuccess);

        // Assert 2: Second victory should work, combat session cleared again
        var load2 = await _service.LoadOperatorAsync(operatorId);
        Assert.Equal(OperatorMode.Infil, load2.Value!.CurrentMode);
        Assert.Null(load2.Value!.ActiveCombatSessionId); // Cleared after second victory
        Assert.NotNull(load2.Value!.InfilSessionId); // Infil session still persists
        Assert.Equal(2, load2.Value!.ExfilStreak);
        Assert.Equal(250, load2.Value!.TotalXp);
    }

    [Fact]
    public async Task AfterVictory_InfilCanBeFailedProgrammatically()
    {
        // This test validates that infil operations can be failed programmatically
        // (e.g., for timeout handling) even when there's no active combat session.
        // Note: User-initiated retreat is NOT supported - operators must complete combat, die, or timeout.
        
        // Arrange: Create operator, start infil, win combat
        var createResult = await _service.CreateOperatorAsync("TestOp2");
        var operatorId = createResult.Value!;

        var infilResult = await _service.StartInfilAsync(operatorId);
        var sessionId = infilResult.Value;

        var victory = new CombatOutcome(
            sessionId: sessionId,
            operatorId: operatorId,
            operatorDied: false,
            damageTaken: 10f,
            xpGained: 50,
            gearLost: Array.Empty<GearId>(),
            isVictory: true,
            completedAt: DateTimeOffset.UtcNow);

        await _service.ProcessCombatOutcomeAsync(victory, playerConfirmed: true);

        // Verify state before failing infil
        var beforeFail = await _service.LoadOperatorAsync(operatorId);
        Assert.Equal(OperatorMode.Infil, beforeFail.Value!.CurrentMode);
        Assert.Null(beforeFail.Value!.ActiveCombatSessionId);
        Assert.Equal(1, beforeFail.Value!.ExfilStreak);

        // Act: Fail infil programmatically (simulates timeout or system-initiated failure)
        var failResult = await _service.FailInfilAsync(operatorId, "Infil timer expired (30 minutes)");
        Assert.True(failResult.IsSuccess);

        // Assert: Operator should be back at base with reset streak
        var afterFail = await _service.LoadOperatorAsync(operatorId);
        Assert.Equal(OperatorMode.Base, afterFail.Value!.CurrentMode);
        Assert.Null(afterFail.Value!.ActiveCombatSessionId);
        Assert.Null(afterFail.Value!.InfilSessionId);
        Assert.Equal(0, afterFail.Value!.ExfilStreak); // Reset on failure
    }

    [Fact]
    public async Task AfterVictory_OperatorCanCompleteInfilSuccessfully()
    {
        // This test validates the fix for the exfil issue where operators
        // couldn't exfil after a victory because ActiveCombatSessionId was cleared.
        // The new CompleteInfilSuccessfullyAsync method allows exfil with no active session.
        
        // Arrange: Create operator, start infil, win combat
        var createResult = await _service.CreateOperatorAsync("TestOp3");
        var operatorId = createResult.Value!;

        var infilResult = await _service.StartInfilAsync(operatorId);
        var sessionId = infilResult.Value;

        var victory = new CombatOutcome(
            sessionId: sessionId,
            operatorId: operatorId,
            operatorDied: false,
            damageTaken: 15f,
            xpGained: 75,
            gearLost: Array.Empty<GearId>(),
            isVictory: true,
            completedAt: DateTimeOffset.UtcNow);

        await _service.ProcessCombatOutcomeAsync(victory, playerConfirmed: true);

        // Verify state after victory - still in Infil mode with no active session
        var afterVictory = await _service.LoadOperatorAsync(operatorId);
        Assert.Equal(OperatorMode.Infil, afterVictory.Value!.CurrentMode);
        Assert.Null(afterVictory.Value!.ActiveCombatSessionId); // Cleared after victory
        Assert.Equal(1, afterVictory.Value!.ExfilStreak);
        Assert.Equal(75, afterVictory.Value!.TotalXp);

        // Act: Complete infil successfully (player chooses to exfil)
        var completeResult = await _service.CompleteInfilSuccessfullyAsync(operatorId);
        Assert.True(completeResult.IsSuccess);

        // Assert: Operator should be back at base with preserved streak and loot
        var afterComplete = await _service.LoadOperatorAsync(operatorId);
        Assert.Equal(OperatorMode.Base, afterComplete.Value!.CurrentMode);
        Assert.Null(afterComplete.Value!.ActiveCombatSessionId);
        Assert.Null(afterComplete.Value!.InfilSessionId);
        Assert.Equal(1, afterComplete.Value!.ExfilStreak); // Preserved on successful completion
        Assert.Equal(75, afterComplete.Value!.TotalXp); // XP preserved
    }
}
