using GUNRPG.Core.VirtualPet;
using Xunit;

namespace GUNRPG.Tests;

public class PetInputTests
{
    [Fact]
    public void RestInput_CanBeCreated_WithDuration()
    {
        // Arrange & Act
        var duration = TimeSpan.FromHours(8);
        var input = new RestInput(duration);

        // Assert
        Assert.Equal(duration, input.Duration);
        Assert.IsAssignableFrom<PetInput>(input);
    }

    [Fact]
    public void EatInput_CanBeCreated_WithNutrition()
    {
        // Arrange & Act
        var input = new EatInput(Nutrition: 50.0f);

        // Assert
        Assert.Equal(50.0f, input.Nutrition);
        Assert.IsAssignableFrom<PetInput>(input);
    }

    [Fact]
    public void DrinkInput_CanBeCreated_WithHydration()
    {
        // Arrange & Act
        var input = new DrinkInput(Hydration: 75.0f);

        // Assert
        Assert.Equal(75.0f, input.Hydration);
        Assert.IsAssignableFrom<PetInput>(input);
    }

    [Fact]
    public void MissionInput_CanBeCreated_WithHitsTakenAndOpponentDifficulty()
    {
        // Arrange & Act
        var input = new MissionInput(HitsTaken: 3, OpponentDifficulty: 75.0f);

        // Assert
        Assert.Equal(3, input.HitsTaken);
        Assert.Equal(75.0f, input.OpponentDifficulty);
        Assert.IsAssignableFrom<PetInput>(input);
    }

    [Fact]
    public void PetInput_DerivedRecords_SupportValueEquality()
    {
        // Arrange
        var rest1 = new RestInput(TimeSpan.FromHours(8));
        var rest2 = new RestInput(TimeSpan.FromHours(8));
        var rest3 = new RestInput(TimeSpan.FromHours(6));

        var eat1 = new EatInput(50.0f);
        var eat2 = new EatInput(50.0f);

        var drink1 = new DrinkInput(75.0f);
        var drink2 = new DrinkInput(75.0f);

        var mission1 = new MissionInput(3, 75.0f);
        var mission2 = new MissionInput(3, 75.0f);

        // Assert
        Assert.Equal(rest1, rest2);
        Assert.NotEqual(rest1, rest3);
        Assert.Equal(eat1, eat2);
        Assert.Equal(drink1, drink2);
        Assert.Equal(mission1, mission2);
    }

    [Fact]
    public void PetInput_DerivedRecords_SupportWithExpression()
    {
        // Arrange
        var originalRest = new RestInput(TimeSpan.FromHours(8));
        var originalMission = new MissionInput(HitsTaken: 3, OpponentDifficulty: 75.0f);

        // Act
        var updatedRest = originalRest with { Duration = TimeSpan.FromHours(10) };
        var updatedMission = originalMission with { HitsTaken = 5 };

        // Assert
        Assert.Equal(TimeSpan.FromHours(8), originalRest.Duration);
        Assert.Equal(TimeSpan.FromHours(10), updatedRest.Duration);
        Assert.Equal(3, originalMission.HitsTaken);
        Assert.Equal(5, updatedMission.HitsTaken);
        Assert.Equal(75.0f, updatedMission.OpponentDifficulty); // Unchanged property
    }

    [Fact]
    public void PetInput_CanBeUsedPolymorphically()
    {
        // Arrange & Act
        PetInput[] inputs = 
        {
            new RestInput(TimeSpan.FromHours(8)),
            new EatInput(50.0f),
            new DrinkInput(75.0f),
            new MissionInput(3, 75.0f)
        };

        // Assert
        Assert.Equal(4, inputs.Length);
        Assert.IsType<RestInput>(inputs[0]);
        Assert.IsType<EatInput>(inputs[1]);
        Assert.IsType<DrinkInput>(inputs[2]);
        Assert.IsType<MissionInput>(inputs[3]);
    }

    [Fact]
    public void PetInput_SupportsPatternMatching()
    {
        // Arrange
        PetInput input = new MissionInput(HitsTaken: 3, OpponentDifficulty: 75.0f);

        // Act & Assert
        var result = input switch
        {
            RestInput rest => $"Rest for {rest.Duration.TotalHours}h",
            EatInput eat => $"Eat {eat.Nutrition} nutrition",
            DrinkInput drink => $"Drink {drink.Hydration} hydration",
            MissionInput mission => $"Mission with {mission.HitsTaken} hits at {mission.OpponentDifficulty} difficulty",
            _ => "Unknown"
        };

        Assert.Equal("Mission with 3 hits at 75 difficulty", result);
    }

    [Fact]
    public void RestInput_AcceptsBoundaryValues()
    {
        // Arrange & Act
        var zeroDuration = new RestInput(TimeSpan.Zero);
        var maxDuration = new RestInput(TimeSpan.MaxValue);

        // Assert
        Assert.Equal(TimeSpan.Zero, zeroDuration.Duration);
        Assert.Equal(TimeSpan.MaxValue, maxDuration.Duration);
    }

    [Fact]
    public void EatInput_AcceptsBoundaryValues()
    {
        // Arrange & Act
        var zero = new EatInput(0.0f);
        var negative = new EatInput(-10.0f);
        var large = new EatInput(1000.0f);

        // Assert
        Assert.Equal(0.0f, zero.Nutrition);
        Assert.Equal(-10.0f, negative.Nutrition);
        Assert.Equal(1000.0f, large.Nutrition);
    }

    [Fact]
    public void DrinkInput_AcceptsBoundaryValues()
    {
        // Arrange & Act
        var zero = new DrinkInput(0.0f);
        var negative = new DrinkInput(-10.0f);
        var large = new DrinkInput(1000.0f);

        // Assert
        Assert.Equal(0.0f, zero.Hydration);
        Assert.Equal(-10.0f, negative.Hydration);
        Assert.Equal(1000.0f, large.Hydration);
    }

    [Fact]
    public void MissionInput_AcceptsBoundaryValues()
    {
        // Arrange & Act
        var zeros = new MissionInput(0, 0.0f);
        var negatives = new MissionInput(-10, -20.0f);
        var large = new MissionInput(1000, 2000.0f);

        // Assert
        Assert.Equal(0, zeros.HitsTaken);
        Assert.Equal(0.0f, zeros.OpponentDifficulty);
        Assert.Equal(-10, negatives.HitsTaken);
        Assert.Equal(-20.0f, negatives.OpponentDifficulty);
        Assert.Equal(1000, large.HitsTaken);
        Assert.Equal(2000.0f, large.OpponentDifficulty);
    }
}
