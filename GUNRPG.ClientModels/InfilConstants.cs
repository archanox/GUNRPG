namespace GUNRPG.ClientModels;

/// <summary>
/// Shared constants for infil (infiltration) rules used by all clients.
/// Server-side enforcement lives in OperatorExfilService; these values must stay in sync.
/// </summary>
public static class InfilConstants
{
    /// <summary>
    /// Maximum duration of a single infil session in minutes.
    /// Operators who do not exfil within this window are automatically failed.
    /// Must match <c>OperatorExfilService.InfilTimerMinutes</c> in GUNRPG.Application.
    /// </summary>
    public const int InfilDurationMinutes = 30;
}
