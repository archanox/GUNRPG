using GUNRPG.Core.VirtualPet;
using Xunit;

namespace GUNRPG.Tests;

public class PetConstantsTests
{
    [Fact]
    public void PetConstants_StatBoundaries_AreCorrectlyDefined()
    {
        // Assert
        Assert.Equal(100f, PetConstants.MaxStatValue);
        Assert.Equal(0f, PetConstants.MinStatValue);
        Assert.True(PetConstants.MaxStatValue > PetConstants.MinStatValue);
    }

    [Fact]
    public void PetConstants_BackgroundDecayRates_ArePositive()
    {
        // Assert - All decay rates should be positive
        Assert.True(PetConstants.HungerIncreasePerHour > 0f);
        Assert.True(PetConstants.HydrationDecreasePerHour > 0f);
        Assert.True(PetConstants.FatigueIncreasePerHour > 0f);
        Assert.True(PetConstants.StressIncreasePerHour > 0f);
    }

    [Fact]
    public void PetConstants_RecoveryRates_ArePositive()
    {
        // Assert - All recovery rates should be positive
        Assert.True(PetConstants.HealthRecoveryPerHour > 0f);
        Assert.True(PetConstants.StressRecoveryPerHour > 0f);
        Assert.True(PetConstants.FatigueRecoveryPerHour > 0f);
    }

    [Fact]
    public void PetConstants_HungerIncreasePerHour_HasReasonableValue()
    {
        // Assert - Should be reasonable for gameplay (not too fast or slow)
        Assert.InRange(PetConstants.HungerIncreasePerHour, 1f, 20f);
    }

    [Fact]
    public void PetConstants_HydrationDecreasePerHour_HasReasonableValue()
    {
        // Assert - Should be reasonable for gameplay (not too fast or slow)
        Assert.InRange(PetConstants.HydrationDecreasePerHour, 1f, 20f);
    }

    [Fact]
    public void PetConstants_FatigueIncreasePerHour_HasReasonableValue()
    {
        // Assert - Should be reasonable for gameplay (not too fast or slow)
        Assert.InRange(PetConstants.FatigueIncreasePerHour, 1f, 30f);
    }

    [Fact]
    public void PetConstants_StressIncreasePerHour_HasReasonableValue()
    {
        // Assert - Should be reasonable for gameplay (not too fast or slow)
        Assert.InRange(PetConstants.StressIncreasePerHour, 1f, 20f);
    }

    [Fact]
    public void PetConstants_HealthRecoveryPerHour_HasReasonableValue()
    {
        // Assert - Should be reasonable for gameplay (not too fast or slow)
        Assert.InRange(PetConstants.HealthRecoveryPerHour, 5f, 50f);
    }

    [Fact]
    public void PetConstants_StressRecoveryPerHour_HasReasonableValue()
    {
        // Assert - Should be reasonable for gameplay (not too fast or slow)
        Assert.InRange(PetConstants.StressRecoveryPerHour, 5f, 50f);
    }

    [Fact]
    public void PetConstants_FatigueRecoveryPerHour_HasReasonableValue()
    {
        // Assert - Should be reasonable for gameplay (not too fast or slow)
        Assert.InRange(PetConstants.FatigueRecoveryPerHour, 5f, 50f);
    }

    [Fact]
    public void PetConstants_DecayAndRecoveryRates_AreBalanced()
    {
        // Assert - Recovery rates should generally be higher than decay rates
        // to allow for meaningful rest periods
        Assert.True(PetConstants.FatigueRecoveryPerHour > PetConstants.FatigueIncreasePerHour,
            "Fatigue recovery should be faster than fatigue increase for balanced gameplay");
        
        Assert.True(PetConstants.StressRecoveryPerHour > PetConstants.StressIncreasePerHour,
            "Stress recovery should be faster than stress increase for balanced gameplay");
    }

    [Fact]
    public void PetConstants_CanCalculateTimeToMaxStat()
    {
        // Arrange - Calculate how long it takes to reach max from min
        float hoursToMaxHunger = (PetConstants.MaxStatValue - PetConstants.MinStatValue) 
            / PetConstants.HungerIncreasePerHour;
        
        float hoursToDepletedHydration = (PetConstants.MaxStatValue - PetConstants.MinStatValue) 
            / PetConstants.HydrationDecreasePerHour;

        // Assert - Should take a reasonable amount of time (not instant, not forever)
        Assert.InRange(hoursToMaxHunger, 1f, 100f);
        Assert.InRange(hoursToDepletedHydration, 1f, 100f);
    }

    [Fact]
    public void PetConstants_CanCalculateRecoveryTime()
    {
        // Arrange - Calculate full recovery time from depleted state
        float hoursToFullHealth = (PetConstants.MaxStatValue - PetConstants.MinStatValue) 
            / PetConstants.HealthRecoveryPerHour;
        
        float hoursToFullFatigueRecovery = (PetConstants.MaxStatValue - PetConstants.MinStatValue) 
            / PetConstants.FatigueRecoveryPerHour;

        // Assert - Should take a reasonable amount of time
        Assert.InRange(hoursToFullHealth, 1f, 50f);
        Assert.InRange(hoursToFullFatigueRecovery, 1f, 50f);
    }

    [Fact]
    public void PetConstants_AllConstants_AreAccessible()
    {
        // Act & Assert - Verify all constants can be read without errors
        var maxStat = PetConstants.MaxStatValue;
        var minStat = PetConstants.MinStatValue;
        var hungerIncrease = PetConstants.HungerIncreasePerHour;
        var hydrationDecrease = PetConstants.HydrationDecreasePerHour;
        var fatigueIncrease = PetConstants.FatigueIncreasePerHour;
        var stressIncrease = PetConstants.StressIncreasePerHour;
        var healthRecovery = PetConstants.HealthRecoveryPerHour;
        var stressRecovery = PetConstants.StressRecoveryPerHour;
        var fatigueRecovery = PetConstants.FatigueRecoveryPerHour;

        // All values should be non-NaN and non-infinite
        Assert.False(float.IsNaN(maxStat));
        Assert.False(float.IsInfinity(maxStat));
        Assert.False(float.IsNaN(minStat));
        Assert.False(float.IsInfinity(minStat));
        Assert.False(float.IsNaN(hungerIncrease));
        Assert.False(float.IsInfinity(hungerIncrease));
        Assert.False(float.IsNaN(hydrationDecrease));
        Assert.False(float.IsInfinity(hydrationDecrease));
        Assert.False(float.IsNaN(fatigueIncrease));
        Assert.False(float.IsInfinity(fatigueIncrease));
        Assert.False(float.IsNaN(stressIncrease));
        Assert.False(float.IsInfinity(stressIncrease));
        Assert.False(float.IsNaN(healthRecovery));
        Assert.False(float.IsInfinity(healthRecovery));
        Assert.False(float.IsNaN(stressRecovery));
        Assert.False(float.IsInfinity(stressRecovery));
        Assert.False(float.IsNaN(fatigueRecovery));
        Assert.False(float.IsInfinity(fatigueRecovery));
    }
}
