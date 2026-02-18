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
        // couldn't engage in consecutive battles after a victory
        
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

        // Assert 1: Operator should be in Infil mode but with no active session
        var load1 = await _service.LoadOperatorAsync(operatorId);
        Assert.Equal(OperatorMode.Infil, load1.Value!.CurrentMode);
        Assert.Null(load1.Value!.ActiveSessionId); // This was the bug - session cleared after victory
        Assert.Equal(1, load1.Value!.ExfilStreak);

        // Act 2: Start second combat (this should work now!)
        var session2 = Guid.NewGuid();
        var victory2 = new CombatOutcome(
            sessionId: session2, // Different session ID
            operatorId: operatorId,
            operatorDied: false,
            damageTaken: 15f,
            xpGained: 150,
            gearLost: Array.Empty<GearId>(),
            isVictory: true,
            completedAt: DateTimeOffset.UtcNow);

        var processResult2 = await _service.ProcessCombatOutcomeAsync(victory2, playerConfirmed: true);
        Assert.True(processResult2.IsSuccess);

        // Assert 2: Second victory should also work
        var load2 = await _service.LoadOperatorAsync(operatorId);
        Assert.Equal(OperatorMode.Infil, load2.Value!.CurrentMode);
        Assert.Null(load2.Value!.ActiveSessionId);
        Assert.Equal(2, load2.Value!.ExfilStreak);
        Assert.Equal(250, load2.Value!.TotalXp);
    }

    [Fact]
    public async Task AfterVictory_OperatorCanRetreat()
    {
        // This test validates that operators can retreat/exfil
        // even when they have no active session (after a victory)
        
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

        // Verify state before retreat
        var beforeRetreat = await _service.LoadOperatorAsync(operatorId);
        Assert.Equal(OperatorMode.Infil, beforeRetreat.Value!.CurrentMode);
        Assert.Null(beforeRetreat.Value!.ActiveSessionId);
        Assert.Equal(1, beforeRetreat.Value!.ExfilStreak);

        // Act: Retreat from infil (without active session)
        var retreatResult = await _service.FailInfilAsync(operatorId, "Operator retreated");
        Assert.True(retreatResult.IsSuccess);

        // Assert: Operator should be back at base with reset streak
        var afterRetreat = await _service.LoadOperatorAsync(operatorId);
        Assert.Equal(OperatorMode.Base, afterRetreat.Value!.CurrentMode);
        Assert.Null(afterRetreat.Value!.ActiveSessionId);
        Assert.Equal(0, afterRetreat.Value!.ExfilStreak); // Reset on retreat
    }
}
