using GUNRPG.WebClient.Helpers;

namespace GUNRPG.Tests;

public sealed class CombatProgressBarHelperTests
{
    [Theory]
    [InlineData(100, 100, 100)]
    [InlineData(50, 100, 50)]
    [InlineData(12, 0, 0)]
    [InlineData(-5, 100, 0)]
    [InlineData(250, 100, 100)]
    [InlineData(24.6, 50, 49)]
    public void GetPercent_ClampsAndRoundsExpectedValues(double current, double maximum, int expected)
    {
        var percent = CombatProgressBarHelper.GetPercent(current, maximum);

        Assert.Equal(expected, percent);
    }

    [Theory]
    [InlineData(25.2, 25)]
    [InlineData(25.5, 26)]
    [InlineData(-10, 0)]
    public void GetAriaValue_ReturnsNonNegativeRoundedValue(double current, int expected)
    {
        var value = CombatProgressBarHelper.GetAriaValue(current);

        Assert.Equal(expected, value);
    }

    [Theory]
    [InlineData(100, 100)]
    [InlineData(100.5, 101)]
    [InlineData(0, 0)]
    [InlineData(-10, 0)]
    public void GetAriaMax_ReturnsRoundedNonNegativeMaximum(double maximum, int expected)
    {
        var value = CombatProgressBarHelper.GetAriaMax(maximum);

        Assert.Equal(expected, value);
    }
}
