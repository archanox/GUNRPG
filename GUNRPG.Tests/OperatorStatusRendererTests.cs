using GUNRPG.Core.Rendering;
using GUNRPG.Core.VirtualPet;
using System.Text;
using Xunit;

namespace GUNRPG.Tests;

public class OperatorStatusRendererTests
{
    [Fact]
    public void Render_OutputsAllPhysicalStats()
    {
        // Arrange
        var view = new OperatorStatusView(
            Health: 75.0f,
            Injury: 10.0f,
            Fatigue: 30.0f,
            Stress: 40.0f,
            Morale: 80.0f,
            Hunger: 25.0f,
            Hydration: 60.0f,
            CombatReadiness: null
        );

        var output = new StringBuilder();
        using var writer = new StringWriter(output);
        var originalOut = Console.Out;
        Console.SetOut(writer);

        try
        {
            // Act
            OperatorStatusRenderer.Render(view);
            var result = output.ToString();

            // Assert
            Assert.Contains("PHYSICAL", result);
            Assert.Contains("Health:   75", result);
            Assert.Contains("Injury:   10", result);
            Assert.Contains("Fatigue:  30", result);
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    [Fact]
    public void Render_OutputsAllMentalStats()
    {
        // Arrange
        var view = new OperatorStatusView(
            Health: 100.0f,
            Injury: 0.0f,
            Fatigue: 0.0f,
            Stress: 45.0f,
            Morale: 85.0f,
            Hunger: 0.0f,
            Hydration: 100.0f,
            CombatReadiness: null
        );

        var output = new StringBuilder();
        using var writer = new StringWriter(output);
        var originalOut = Console.Out;
        Console.SetOut(writer);

        try
        {
            // Act
            OperatorStatusRenderer.Render(view);
            var result = output.ToString();

            // Assert
            Assert.Contains("MENTAL", result);
            Assert.Contains("Stress:   45", result);
            Assert.Contains("Morale:   85", result);
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    [Fact]
    public void Render_OutputsAllCareStats()
    {
        // Arrange
        var view = new OperatorStatusView(
            Health: 100.0f,
            Injury: 0.0f,
            Fatigue: 0.0f,
            Stress: 0.0f,
            Morale: 100.0f,
            Hunger: 35.0f,
            Hydration: 65.0f,
            CombatReadiness: null
        );

        var output = new StringBuilder();
        using var writer = new StringWriter(output);
        var originalOut = Console.Out;
        Console.SetOut(writer);

        try
        {
            // Act
            OperatorStatusRenderer.Render(view);
            var result = output.ToString();

            // Assert
            Assert.Contains("CARE", result);
            Assert.Contains("Hunger:     35", result);
            Assert.Contains("Hydration:  65", result);
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    [Fact]
    public void Render_ShowsCombatReadiness_WhenPresent()
    {
        // Arrange
        var view = new OperatorStatusView(
            Health: 100.0f,
            Injury: 0.0f,
            Fatigue: 0.0f,
            Stress: 0.0f,
            Morale: 100.0f,
            Hunger: 0.0f,
            Hydration: 100.0f,
            CombatReadiness: 92.0f
        );

        var output = new StringBuilder();
        using var writer = new StringWriter(output);
        var originalOut = Console.Out;
        Console.SetOut(writer);

        try
        {
            // Act
            OperatorStatusRenderer.Render(view);
            var result = output.ToString();

            // Assert
            Assert.Contains("DERIVED", result);
            Assert.Contains("Combat Readiness:  92", result);
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    [Fact]
    public void Render_HidesCombatReadiness_WhenNull()
    {
        // Arrange
        var view = new OperatorStatusView(
            Health: 100.0f,
            Injury: 0.0f,
            Fatigue: 0.0f,
            Stress: 0.0f,
            Morale: 100.0f,
            Hunger: 0.0f,
            Hydration: 100.0f,
            CombatReadiness: null
        );

        var output = new StringBuilder();
        using var writer = new StringWriter(output);
        var originalOut = Console.Out;
        Console.SetOut(writer);

        try
        {
            // Act
            OperatorStatusRenderer.Render(view);
            var result = output.ToString();

            // Assert
            Assert.DoesNotContain("DERIVED", result);
            Assert.DoesNotContain("Combat Readiness", result);
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    [Fact]
    public void Render_FormatsValuesWithNoDecimals()
    {
        // Arrange
        var view = new OperatorStatusView(
            Health: 75.7f,
            Injury: 10.3f,
            Fatigue: 30.9f,
            Stress: 40.2f,
            Morale: 80.8f,
            Hunger: 25.1f,
            Hydration: 60.6f,
            CombatReadiness: null
        );

        var output = new StringBuilder();
        using var writer = new StringWriter(output);
        var originalOut = Console.Out;
        Console.SetOut(writer);

        try
        {
            // Act
            OperatorStatusRenderer.Render(view);
            var result = output.ToString();

            // Assert - Values should be rounded to whole numbers
            Assert.Contains("Health:   76", result);
            Assert.Contains("Injury:   10", result);
            Assert.Contains("Fatigue:  31", result);
            Assert.Contains("Stress:   40", result);
            Assert.Contains("Morale:   81", result);
            Assert.Contains("Hunger:     25", result);
            Assert.Contains("Hydration:  61", result);
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    [Fact]
    public void Render_IncludesASCIISeparators()
    {
        // Arrange
        var view = new OperatorStatusView(
            Health: 50.0f,
            Injury: 50.0f,
            Fatigue: 50.0f,
            Stress: 50.0f,
            Morale: 50.0f,
            Hunger: 50.0f,
            Hydration: 50.0f,
            CombatReadiness: null
        );

        var output = new StringBuilder();
        using var writer = new StringWriter(output);
        var originalOut = Console.Out;
        Console.SetOut(writer);

        try
        {
            // Act
            OperatorStatusRenderer.Render(view);
            var result = output.ToString();

            // Assert - Check for ASCII separators with context
            Assert.Contains("================================================================================", result);
            Assert.Contains("OPERATOR STATUS", result);
            Assert.Contains("PHYSICAL\n--------", result);
            Assert.Contains("MENTAL\n------", result);
            Assert.Contains("CARE\n----", result);
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    [Fact]
    public void Render_HandlesMinimumValues()
    {
        // Arrange
        var view = new OperatorStatusView(
            Health: 0.0f,
            Injury: 0.0f,
            Fatigue: 0.0f,
            Stress: 0.0f,
            Morale: 0.0f,
            Hunger: 0.0f,
            Hydration: 0.0f,
            CombatReadiness: null
        );

        var output = new StringBuilder();
        using var writer = new StringWriter(output);
        var originalOut = Console.Out;
        Console.SetOut(writer);

        try
        {
            // Act
            OperatorStatusRenderer.Render(view);
            var result = output.ToString();

            // Assert - All values should be 0
            // Note: Spacing varies by label length to accommodate right-aligned 3-digit formatting
            // Format: "Label: " + "{value,3:F0}" where value,3 right-aligns in 3 chars
            Assert.Contains("Health:    0", result);    // "  0" (2 spaces before 0)
            Assert.Contains("Injury:    0", result);    // "  0" (2 spaces before 0)
            Assert.Contains("Fatigue:   0", result);    // "  0" (2 spaces before 0)
            Assert.Contains("Stress:    0", result);    // "  0" (2 spaces before 0)
            Assert.Contains("Morale:    0", result);    // "  0" (2 spaces before 0)
            Assert.Contains("Hunger:      0", result);  // "  0" (2 spaces before 0, 4 after colon)
            Assert.Contains("Hydration:   0", result);  // "  0" (2 spaces before 0, 1 after colon)
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    [Fact]
    public void Render_HandlesMaximumValues()
    {
        // Arrange
        var view = new OperatorStatusView(
            Health: 100.0f,
            Injury: 100.0f,
            Fatigue: 100.0f,
            Stress: 100.0f,
            Morale: 100.0f,
            Hunger: 100.0f,
            Hydration: 100.0f,
            CombatReadiness: 100.0f
        );

        var output = new StringBuilder();
        using var writer = new StringWriter(output);
        var originalOut = Console.Out;
        Console.SetOut(writer);

        try
        {
            // Act
            OperatorStatusRenderer.Render(view);
            var result = output.ToString();

            // Assert - All values should be 100
            Assert.Contains("Health:  100", result);
            Assert.Contains("Injury:  100", result);
            Assert.Contains("Fatigue: 100", result);
            Assert.Contains("Stress:  100", result);
            Assert.Contains("Morale:  100", result);
            Assert.Contains("Hunger:    100", result);
            Assert.Contains("Hydration: 100", result);
            Assert.Contains("Combat Readiness: 100", result);
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }
}
