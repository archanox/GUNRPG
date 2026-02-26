namespace GUNRPG.Tests;

public sealed class RaidTimerTests
{
    [Fact]
    public void ComputeRemaining_UsesAbsoluteElapsedUtcTime()
    {
        var start = new DateTime(2026, 2, 25, 12, 0, 0, DateTimeKind.Utc);
        var now = start.AddMinutes(5).AddSeconds(10);

        var remaining = RaidTimer.ComputeRemaining(start, TimeSpan.FromMinutes(30), now);

        Assert.Equal(TimeSpan.FromMinutes(24).Add(TimeSpan.FromSeconds(50)), remaining);
    }

    [Fact]
    public void FormatRemainingLabel_UsesCriticalPrefixWhenUnderFiveSeconds()
    {
        var label = RaidTimer.FormatRemainingLabel(TimeSpan.FromSeconds(4));

        Assert.Equal("🚨 EXFIL WINDOW: 00:04 REMAINING", label);
    }

    [Fact]
    public void FormatStyledRemainingLabel_UsesRedBlinkForCriticalWindow()
    {
        var label = RaidTimer.FormatStyledRemainingLabel(TimeSpan.FromSeconds(4));

        Assert.Contains("\u001b[31m", label);
        Assert.Contains("\u001b[5m", label);
    }

    [Fact]
    public void FormatRemainingLabel_UsesUpdatedWarningIcons()
    {
        var medium = RaidTimer.FormatRemainingLabel(TimeSpan.FromSeconds(25));
        var high = RaidTimer.FormatRemainingLabel(TimeSpan.FromSeconds(8));

        Assert.StartsWith("⚠️ ", medium);
        Assert.StartsWith("⚠︎ ", high);
    }
}
