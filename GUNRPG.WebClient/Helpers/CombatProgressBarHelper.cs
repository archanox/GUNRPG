namespace GUNRPG.WebClient.Helpers;

public static class CombatProgressBarHelper
{
    public static int GetPercent(double current, double maximum)
    {
        if (maximum <= 0 || double.IsNaN(current) || double.IsNaN(maximum))
            return 0;

        var percent = current / maximum * 100d;
        return (int)Math.Round(Math.Clamp(percent, 0d, 100d), MidpointRounding.AwayFromZero);
    }

    public static int GetAriaValue(double current)
    {
        if (double.IsNaN(current))
            return 0;

        return (int)Math.Round(Math.Max(current, 0d), MidpointRounding.AwayFromZero);
    }

    public static int GetAriaMax(double maximum)
    {
        if (maximum <= 0 || double.IsNaN(maximum))
            return 0;

        return (int)Math.Round(maximum, MidpointRounding.AwayFromZero);
    }
}
