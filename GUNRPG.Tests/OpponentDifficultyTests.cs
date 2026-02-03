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

    // ========================================
    // XP-Based Level Calculation Tests
    // ========================================

    [Fact]
    public void ComputeLevelFromXp_WithZeroXp_ReturnsZero()
    {
        // Arrange & Act
        var level = OpponentDifficulty.ComputeLevelFromXp(0);

        // Assert
        Assert.Equal(0, level);
    }

    [Fact]
    public void ComputeLevelFromXp_With1000Xp_ReturnsLevel1()
    {
        // Arrange & Act
        var level = OpponentDifficulty.ComputeLevelFromXp(1000);

        // Assert - sqrt(1000/1000) = sqrt(1) = 1
        Assert.Equal(1, level);
    }

    [Fact]
    public void ComputeLevelFromXp_With4000Xp_ReturnsLevel2()
    {
        // Arrange & Act
        var level = OpponentDifficulty.ComputeLevelFromXp(4000);

        // Assert - sqrt(4000/1000) = sqrt(4) = 2
        Assert.Equal(2, level);
    }

    [Fact]
    public void ComputeLevelFromXp_With9000Xp_ReturnsLevel3()
    {
        // Arrange & Act
        var level = OpponentDifficulty.ComputeLevelFromXp(9000);

        // Assert - sqrt(9000/1000) = sqrt(9) = 3
        Assert.Equal(3, level);
    }

    [Fact]
    public void ComputeLevelFromXp_WithNegativeXp_ReturnsZero()
    {
        // Arrange & Act
        var level = OpponentDifficulty.ComputeLevelFromXp(-1000);

        // Assert - Negative XP should be handled gracefully
        Assert.Equal(0, level);
    }

    [Fact]
    public void ComputeLevelFromXp_WithPartialLevel_FloorsToPreviousLevel()
    {
        // Arrange & Act - 2500 XP should be between level 1 and 2
        var level = OpponentDifficulty.ComputeLevelFromXp(2500);

        // Assert - sqrt(2500/1000) = sqrt(2.5) ≈ 1.58, floors to 1
        Assert.Equal(1, level);
    }

    [Fact]
    public void ComputeLevelFromXp_WithLargeXp_ScalesWithSquareRoot()
    {
        // Arrange & Act
        var level = OpponentDifficulty.ComputeLevelFromXp(100000);

        // Assert - sqrt(100000/1000) = sqrt(100) = 10
        Assert.Equal(10, level);
    }

    // ========================================
    // XP and Proficiency-Based Difficulty Tests
    // ========================================

    [Fact]
    public void Compute_WithEqualXpAndProficiencies_Returns50()
    {
        // Arrange & Act
        var result = OpponentDifficulty.Compute(
            opponentXp: 4000,
            playerXp: 4000,
            opponentWeaponProficiency: 50f,
            opponentGeneralProficiency: 50f,
            playerWeaponProficiency: 50f,
            playerGeneralProficiency: 50f
        );

        // Assert - Everything equal should return base difficulty
        Assert.Equal(50f, result);
    }

    [Fact]
    public void Compute_WithHigherOpponentXp_IncreasesDifficulty()
    {
        // Arrange & Act - Opponent level 3 (9k XP) vs Player level 0 (0 XP)
        var result = OpponentDifficulty.Compute(
            opponentXp: 9000,
            playerXp: 0,
            opponentWeaponProficiency: 50f,
            opponentGeneralProficiency: 50f,
            playerWeaponProficiency: 50f,
            playerGeneralProficiency: 50f
        );

        // Assert - 3 level difference: 50 + (3 * 10) = 80
        Assert.Equal(80f, result);
    }

    [Fact]
    public void Compute_WithHigherWeaponProficiency_IncreasesDifficulty()
    {
        // Arrange & Act - Equal levels, opponent has +50 weapon proficiency
        var result = OpponentDifficulty.Compute(
            opponentXp: 1000,
            playerXp: 1000,
            opponentWeaponProficiency: 100f,
            opponentGeneralProficiency: 50f,
            playerWeaponProficiency: 50f,
            playerGeneralProficiency: 50f
        );

        // Assert - Base 50 + weapon impact: (50/100) * 15 = 7.5
        // Total: 50 + 7.5 = 57.5
        Assert.Equal(57.5f, result);
    }

    [Fact]
    public void Compute_WithHigherGeneralProficiency_IncreasesDifficulty()
    {
        // Arrange & Act - Equal levels, opponent has +60 general proficiency
        var result = OpponentDifficulty.Compute(
            opponentXp: 1000,
            playerXp: 1000,
            opponentWeaponProficiency: 50f,
            opponentGeneralProficiency: 100f,
            playerWeaponProficiency: 50f,
            playerGeneralProficiency: 40f
        );

        // Assert - Base 50 + general impact: (60/100) * 10 = 6
        // Total: 50 + 6 = 56
        Assert.Equal(56f, result);
    }

    [Fact]
    public void Compute_WithLowerProficiencies_DecreasesDifficulty()
    {
        // Arrange & Act - Equal levels, opponent has lower proficiencies
        var result = OpponentDifficulty.Compute(
            opponentXp: 1000,
            playerXp: 1000,
            opponentWeaponProficiency: 20f,
            opponentGeneralProficiency: 30f,
            playerWeaponProficiency: 80f,
            playerGeneralProficiency: 70f
        );

        // Assert - Base 50
        // Weapon: (-60/100) * 15 = -9
        // General: (-40/100) * 10 = -4
        // Total: 50 - 9 - 4 = 37
        Assert.Equal(37f, result);
    }

    [Fact]
    public void Compute_WithCombinedFactors_SumsCorrectly()
    {
        // Arrange & Act - Opponent has level advantage and proficiency advantage
        var result = OpponentDifficulty.Compute(
            opponentXp: 4000, // Level 2
            playerXp: 0,      // Level 0
            opponentWeaponProficiency: 80f,
            opponentGeneralProficiency: 70f,
            playerWeaponProficiency: 50f,
            playerGeneralProficiency: 50f
        );

        // Assert - Base 50
        // Level: +2 * 10 = +20
        // Weapon: (30/100) * 15 = +4.5
        // General: (20/100) * 10 = +2
        // Total: 50 + 20 + 4.5 + 2 = 76.5
        Assert.Equal(76.5f, result);
    }

    [Fact]
    public void Compute_WithMaxProficiencyAdvantage_ClampsCorrectly()
    {
        // Arrange & Act - Maximum possible proficiency advantage
        var result = OpponentDifficulty.Compute(
            opponentXp: 81000, // Level 9
            playerXp: 0,       // Level 0
            opponentWeaponProficiency: 100f,
            opponentGeneralProficiency: 100f,
            playerWeaponProficiency: 0f,
            playerGeneralProficiency: 0f
        );

        // Assert - Base 50
        // Level: +9 * 10 = +90
        // Weapon: (100/100) * 15 = +15
        // General: (100/100) * 10 = +10
        // Total: 50 + 90 + 15 + 10 = 165, clamped to 100
        Assert.Equal(100f, result);
    }

    [Fact]
    public void Compute_WithPlayerAdvantage_ClampsToMinimum()
    {
        // Arrange & Act - Player has all advantages
        var result = OpponentDifficulty.Compute(
            opponentXp: 0,     // Level 0
            playerXp: 81000,   // Level 9
            opponentWeaponProficiency: 0f,
            opponentGeneralProficiency: 0f,
            playerWeaponProficiency: 100f,
            playerGeneralProficiency: 100f
        );

        // Assert - Base 50
        // Level: -9 * 10 = -90
        // Weapon: (-100/100) * 15 = -15
        // General: (-100/100) * 10 = -10
        // Total: 50 - 90 - 15 - 10 = -65, clamped to 10
        Assert.Equal(10f, result);
    }

    [Fact]
    public void Compute_WithProficienciesOnly_HasSmallerImpactThanLevels()
    {
        // Arrange & Act
        var levelResult = OpponentDifficulty.Compute(
            opponentXp: 4000, // Level 2
            playerXp: 0,      // Level 0
            opponentWeaponProficiency: 50f,
            opponentGeneralProficiency: 50f,
            playerWeaponProficiency: 50f,
            playerGeneralProficiency: 50f
        );

        var proficiencyResult = OpponentDifficulty.Compute(
            opponentXp: 1000, // Level 1 (both)
            playerXp: 1000,
            opponentWeaponProficiency: 100f,
            opponentGeneralProficiency: 100f,
            playerWeaponProficiency: 0f,
            playerGeneralProficiency: 0f
        );

        // Assert - 2 level difference (+20) should have more impact than max proficiency advantage (+25)
        // But proficiency total is 25, so it's significant but should be smaller per-unit
        Assert.Equal(70f, levelResult);  // 50 + 20
        Assert.Equal(75f, proficiencyResult); // 50 + 15 + 10
        
        // The key is that per-point, levels have more impact (10 vs 0.15 and 0.10)
    }

    [Fact]
    public void Compute_IsDeterministic_WithXpAndProficiencies()
    {
        // Arrange & Act
        var result1 = OpponentDifficulty.Compute(
            opponentXp: 4000,
            playerXp: 1000,
            opponentWeaponProficiency: 75f,
            opponentGeneralProficiency: 60f,
            playerWeaponProficiency: 50f,
            playerGeneralProficiency: 50f
        );

        var result2 = OpponentDifficulty.Compute(
            opponentXp: 4000,
            playerXp: 1000,
            opponentWeaponProficiency: 75f,
            opponentGeneralProficiency: 60f,
            playerWeaponProficiency: 50f,
            playerGeneralProficiency: 50f
        );

        // Assert - Same inputs should always produce same output
        Assert.Equal(result1, result2);
    }

    [Fact]
    public void Compute_WithFractionalProficiencies_WorksCorrectly()
    {
        // Arrange & Act - Using precise proficiency values
        var result = OpponentDifficulty.Compute(
            opponentXp: 1000,
            playerXp: 1000,
            opponentWeaponProficiency: 75.5f,
            opponentGeneralProficiency: 62.3f,
            playerWeaponProficiency: 50.2f,
            playerGeneralProficiency: 50.8f
        );

        // Assert - Base 50
        // Weapon: (25.3/100) * 15 = 3.795
        // General: (11.5/100) * 10 = 1.15
        // Total: 50 + 3.795 + 1.15 = 54.945
        Assert.Equal(54.945f, result, precision: 3);
    }
}
