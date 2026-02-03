using GUNRPG.Core.VirtualPet;
using Xunit;

namespace GUNRPG.Tests;

public class OpponentDifficultyTests
{
    [Fact]
    public void Compute_WithEqualLevels_Returns50()
    {
        // Arrange & Act
        var result = OpponentDifficulty.Compute(opponentLevel: 5, playerLevel: 5);

        // Assert - Equal levels should return base difficulty of 50
        Assert.Equal(50f, result);
    }

    [Fact]
    public void Compute_WithOpponentHigher_IncreasesDifficulty()
    {
        // Arrange & Act
        var result = OpponentDifficulty.Compute(opponentLevel: 8, playerLevel: 5);

        // Assert - Opponent 3 levels higher: 50 + (3 * 10) = 80
        Assert.Equal(80f, result);
    }

    [Fact]
    public void Compute_WithOpponentLower_DecreasesDifficulty()
    {
        // Arrange & Act
        var result = OpponentDifficulty.Compute(opponentLevel: 5, playerLevel: 8);

        // Assert - Opponent 3 levels lower: 50 + (-3 * 10) = 20
        Assert.Equal(20f, result);
    }

    [Fact]
    public void Compute_WithOpponentOneLevelHigher_Returns60()
    {
        // Arrange & Act
        var result = OpponentDifficulty.Compute(opponentLevel: 6, playerLevel: 5);

        // Assert - Opponent 1 level higher: 50 + (1 * 10) = 60
        Assert.Equal(60f, result);
    }

    [Fact]
    public void Compute_WithOpponentOneLevelLower_Returns40()
    {
        // Arrange & Act
        var result = OpponentDifficulty.Compute(opponentLevel: 4, playerLevel: 5);

        // Assert - Opponent 1 level lower: 50 + (-1 * 10) = 40
        Assert.Equal(40f, result);
    }

    [Fact]
    public void Compute_ClampsToMaximum100()
    {
        // Arrange & Act
        var result = OpponentDifficulty.Compute(opponentLevel: 20, playerLevel: 1);

        // Assert - Level delta would be 190, but should clamp to 100
        Assert.Equal(100f, result);
    }

    [Fact]
    public void Compute_ClampsToMinimum10()
    {
        // Arrange & Act
        var result = OpponentDifficulty.Compute(opponentLevel: 1, playerLevel: 20);

        // Assert - Level delta would be -190, but should clamp to 10
        Assert.Equal(10f, result);
    }

    [Fact]
    public void Compute_WithLargeLevelDifference_IsClamped()
    {
        // Arrange & Act
        var highResult = OpponentDifficulty.Compute(opponentLevel: 100, playerLevel: 1);
        var lowResult = OpponentDifficulty.Compute(opponentLevel: 1, playerLevel: 100);

        // Assert - Both should be clamped
        Assert.Equal(100f, highResult);
        Assert.Equal(10f, lowResult);
    }

    [Fact]
    public void Compute_WithNegativeLevels_WorksCorrectly()
    {
        // Arrange & Act
        var result = OpponentDifficulty.Compute(opponentLevel: -5, playerLevel: -5);

        // Assert - Equal levels regardless of sign should return 50
        Assert.Equal(50f, result);
    }

    [Fact]
    public void Compute_WithZeroLevels_Returns50()
    {
        // Arrange & Act
        var result = OpponentDifficulty.Compute(opponentLevel: 0, playerLevel: 0);

        // Assert - Both at zero should return base difficulty
        Assert.Equal(50f, result);
    }

    [Fact]
    public void Compute_IsDeterministic()
    {
        // Arrange & Act
        var result1 = OpponentDifficulty.Compute(opponentLevel: 10, playerLevel: 5);
        var result2 = OpponentDifficulty.Compute(opponentLevel: 10, playerLevel: 5);

        // Assert - Same inputs should always produce same output
        Assert.Equal(result1, result2);
    }

    [Fact]
    public void Compute_WithVariousLevelDeltas_ScalesLinearly()
    {
        // Arrange & Act & Assert
        // Test linear scaling across various deltas
        Assert.Equal(50f, OpponentDifficulty.Compute(5, 5));  // Delta 0
        Assert.Equal(60f, OpponentDifficulty.Compute(6, 5));  // Delta +1
        Assert.Equal(70f, OpponentDifficulty.Compute(7, 5));  // Delta +2
        Assert.Equal(40f, OpponentDifficulty.Compute(4, 5));  // Delta -1
        Assert.Equal(30f, OpponentDifficulty.Compute(3, 5));  // Delta -2
        Assert.Equal(90f, OpponentDifficulty.Compute(9, 5));  // Delta +4
        Assert.Equal(10f, OpponentDifficulty.Compute(1, 5));  // Delta -4 (clamped)
    }

    [Fact]
    public void Compute_ReturnsFloat()
    {
        // Arrange & Act
        var result = OpponentDifficulty.Compute(opponentLevel: 5, playerLevel: 5);

        // Assert - Result should be a float type
        Assert.IsType<float>(result);
    }

    [Fact]
    public void Compute_WithMixedPositiveAndNegativeLevels_WorksCorrectly()
    {
        // Arrange & Act
        var result1 = OpponentDifficulty.Compute(opponentLevel: 5, playerLevel: -5);
        var result2 = OpponentDifficulty.Compute(opponentLevel: -5, playerLevel: 5);

        // Assert
        // Delta +10: 50 + 100 = 150, clamped to 100
        Assert.Equal(100f, result1);
        // Delta -10: 50 - 100 = -50, clamped to 10
        Assert.Equal(10f, result2);
    }

    [Fact]
    public void Compute_BoundaryCase_JustBelowMaxClamp()
    {
        // Arrange & Act - Level delta that equals exactly +5 (right at 100)
        var result = OpponentDifficulty.Compute(opponentLevel: 10, playerLevel: 5);

        // Assert - 50 + (5 * 10) = 100 (exactly at boundary)
        Assert.Equal(100f, result);
    }

    [Fact]
    public void Compute_BoundaryCase_JustBelowMinClamp()
    {
        // Arrange & Act - Level delta that equals exactly -4 (right at 10)
        var result = OpponentDifficulty.Compute(opponentLevel: 5, playerLevel: 9);

        // Assert - 50 + (-4 * 10) = 10 (exactly at boundary)
        Assert.Equal(10f, result);
    }
}
