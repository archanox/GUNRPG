namespace GUNRPG.WebClient.Helpers;

/// <summary>
/// Shared helpers for the exfil countdown timer displayed on infil and combat screens.
/// </summary>
internal static class ExfilCountdownHelper
{
    public static TimeSpan? GetRemaining(DateTimeOffset? infilStartTime, TimeSpan raidDuration)
    {
        if (infilStartTime is null)
            return null;

        return raidDuration - (DateTimeOffset.UtcNow - infilStartTime.Value);
    }

    public static string FormatRemaining(TimeSpan? remaining)
    {
        if (remaining is null) return "--:--";
        if (remaining.Value <= TimeSpan.Zero) return "00:00";
        return remaining.Value.ToString(@"mm\:ss");
    }

    public static string GetTimerStyle(TimeSpan? remaining)
    {
        if (remaining is null) return string.Empty;
        if (remaining.Value <= TimeSpan.FromSeconds(10)) return "font-weight: bold; color: var(--error);";
        if (remaining.Value <= TimeSpan.FromSeconds(30)) return "font-weight: bold; color: darkorange;";
        return "color: green;";
    }
}
