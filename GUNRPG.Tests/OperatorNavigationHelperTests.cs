using GUNRPG.ClientModels;
using GUNRPG.WebClient.Helpers;

namespace GUNRPG.Tests;

public sealed class OperatorNavigationHelperTests
{
    [Fact]
    public void GetRealtimeRoute_ReturnsNull_WhenOperatorIsNotOnMission()
    {
        var operatorState = new OperatorState
        {
            Id = Guid.NewGuid(),
            CurrentMode = "Base"
        };

        var route = OperatorNavigationHelper.GetRealtimeRoute(operatorState, operatorState.Id);

        Assert.Null(route);
    }

    [Fact]
    public void GetRealtimeRoute_ReturnsInfilRoute_WhenOperatorIsInfilWithoutActiveCombat()
    {
        var operatorId = Guid.NewGuid();
        var operatorState = new OperatorState
        {
            Id = operatorId,
            CurrentMode = "Infil"
        };

        var route = OperatorNavigationHelper.GetRealtimeRoute(operatorState, operatorId);

        Assert.Equal($"missions/infil/{operatorId}", route);
    }

    [Fact]
    public void GetRealtimeRoute_ReturnsCombatRoute_WhenOperatorHasActiveCombat()
    {
        var operatorId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var operatorState = new OperatorState
        {
            Id = operatorId,
            CurrentMode = "Infil",
            ActiveCombatSessionId = sessionId,
            ActiveCombatSession = new CombatSession
            {
                Id = sessionId,
                Phase = "Planning"
            }
        };

        var route = OperatorNavigationHelper.GetRealtimeRoute(operatorState, operatorId);

        Assert.Equal($"missions/{sessionId}?operatorId={operatorId}", route);
    }

    [Fact]
    public void GetRealtimeRoute_ReturnsInfilRoute_WhenCombatSessionIsConcluded()
    {
        var operatorId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var operatorState = new OperatorState
        {
            Id = operatorId,
            CurrentMode = "Infil",
            ActiveCombatSessionId = sessionId,
            ActiveCombatSession = new CombatSession
            {
                Id = sessionId,
                Phase = "Completed"
            }
        };

        var route = OperatorNavigationHelper.GetRealtimeRoute(operatorState, operatorId);

        Assert.Equal($"missions/infil/{operatorId}", route);
    }
}
