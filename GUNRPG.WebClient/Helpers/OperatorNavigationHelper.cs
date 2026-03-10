using GUNRPG.ClientModels;

namespace GUNRPG.WebClient.Helpers;

public static class OperatorNavigationHelper
{
    public static string? GetRealtimeRoute(OperatorState? operatorState, Guid operatorId)
    {
        if (operatorState is null || operatorState.CurrentMode != "Infil")
            return null;

        if (operatorState.ActiveCombatSessionId.HasValue &&
            operatorState.ActiveCombatSession?.IsConcluded == false)
        {
            return $"missions/{operatorState.ActiveCombatSessionId}?operatorId={operatorId}";
        }

        return $"missions/infil/{operatorId}";
    }
}
