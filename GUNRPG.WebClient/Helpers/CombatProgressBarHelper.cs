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

    public static int? GetAriaValue(double current, double maximum)
    {
        if (double.IsNaN(current) || double.IsNaN(maximum) || maximum <= 0d)
            return null;

        var clamped = Math.Clamp(current, 0d, maximum);
        return (int)Math.Round(clamped, MidpointRounding.AwayFromZero);
    }

    public static int? GetAriaMax(double maximum)
    {
        if (maximum <= 0 || double.IsNaN(maximum))
            return null;

        return (int)Math.Round(maximum, MidpointRounding.AwayFromZero);
    }
}
