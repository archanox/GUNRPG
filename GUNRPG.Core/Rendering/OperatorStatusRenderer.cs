using GUNRPG.Core.VirtualPet;

namespace GUNRPG.Core.Rendering;

/// <summary>
/// Temporary, replaceable text-based renderer for operator status.
/// This is NOT the final TUI. This is a simple presentation layer for debugging and gameplay visibility.
/// </summary>
/// <remarks>
/// This renderer will be replaced by a proper TUI in the future.
/// </remarks>
public static class OperatorStatusRenderer
{
    /// <summary>
    /// Renders operator status to the console.
    /// Displays all operator stats in clear labeled sections with ASCII separators for readability.
    /// </summary>
    /// <param name="view">The operator status view to render.</param>
    public static void Render(OperatorStatusView view)
    {
        Console.WriteLine("================================================================================");
        Console.WriteLine("                          OPERATOR STATUS");
        Console.WriteLine("================================================================================");
        Console.WriteLine();

        // Physical section
        Console.WriteLine("PHYSICAL");
        Console.WriteLine("--------");
        Console.WriteLine($"  Health:  {view.Health,3:F0}");
        Console.WriteLine($"  Injury:  {view.Injury,3:F0}");
        Console.WriteLine($"  Fatigue: {view.Fatigue,3:F0}");
        Console.WriteLine();

        // Mental section
        Console.WriteLine("MENTAL");
        Console.WriteLine("------");
        Console.WriteLine($"  Stress:  {view.Stress,3:F0}");
        Console.WriteLine($"  Morale:  {view.Morale,3:F0}");
        Console.WriteLine();

        // Care section
        Console.WriteLine("CARE");
        Console.WriteLine("----");
        Console.WriteLine($"  Hunger:    {view.Hunger,3:F0}");
        Console.WriteLine($"  Hydration: {view.Hydration,3:F0}");
        Console.WriteLine();

        // Derived values (only shown if present)
        if (view.CombatReadiness.HasValue)
        {
            Console.WriteLine("DERIVED");
            Console.WriteLine("-------");
            Console.WriteLine($"  Combat Readiness: {view.CombatReadiness.Value,3:F0}");
            Console.WriteLine();
        }

        Console.WriteLine("================================================================================");
    }
}
