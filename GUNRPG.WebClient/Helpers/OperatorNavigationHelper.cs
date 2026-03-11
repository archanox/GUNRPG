using GUNRPG.ClientModels;

namespace GUNRPG.WebClient.Helpers;

public static class OperatorNavigationHelper
{
    public static bool HasActiveCombat(OperatorState? operatorState) =>
        operatorState is not null &&
        operatorState.IsOnMission &&
        operatorState.ActiveCombatSessionId.HasValue &&
        operatorState.ActiveCombatSession?.IsConcluded != true;

    public static string? GetRealtimeRoute(OperatorState? operatorState, Guid operatorId)
    {
        if (operatorState is null || !operatorState.IsOnMission)
            return null;

        if (HasActiveCombat(operatorState))
        {
            return $"missions/{operatorState.ActiveCombatSessionId}?operatorId={operatorId}";
        }

        return $"missions/infil/{operatorId}";
    }
}
