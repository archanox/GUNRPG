namespace GUNRPG.Core.VirtualPet;

/// <summary>
/// Static utility class for computing opponent difficulty based on level differences.
/// Provides a simple, deterministic difficulty scaling for mission calculations.
/// </summary>
public static class OpponentDifficulty
{
    /// <summary>
    /// Computes the difficulty rating of an opponent based on level difference.
    /// </summary>
    /// <param name="opponentLevel">The level of the opponent.</param>
    /// <param name="playerLevel">The level of the player/operator.</param>
    /// <returns>
    /// A difficulty rating between 10 and 100, where:
    /// - 50 represents an evenly matched opponent (same level)
    /// - Each level difference adjusts difficulty by 10 points
    /// - Higher opponent levels increase difficulty
    /// - Lower opponent levels decrease difficulty
    /// - Result is clamped to the range [10, 100]
    /// </returns>
    /// <remarks>
    /// This is a pure utility function with no dependencies on PetState or combat logic.
    /// The difficulty value can be used as input to mission calculations.
    /// 
    /// Examples:
    /// - Player level 5 vs Opponent level 5: Difficulty = 50 (evenly matched)
    /// - Player level 5 vs Opponent level 8: Difficulty = 80 (opponent 3 levels higher)
    /// - Player level 8 vs Opponent level 5: Difficulty = 20 (opponent 3 levels lower)
    /// - Player level 1 vs Opponent level 20: Difficulty = 100 (clamped at maximum)
    /// </remarks>
    public static float Compute(int opponentLevel, int playerLevel)
    {
        // Calculate level difference (positive when opponent is stronger)
        int levelDelta = opponentLevel - playerLevel;

        // Base difficulty is 50 (evenly matched)
        // Each level of difference adjusts by 10 points
        float difficulty = 50f + (levelDelta * 10f);

        // Clamp result between 10 and 100
        return Math.Clamp(difficulty, 10f, 100f);
    }
}
