namespace GUNRPG.Core.VirtualPet;

/// <summary>
/// Static utility class for computing opponent difficulty based on level differences and proficiencies.
/// Provides deterministic difficulty scaling for mission calculations with no dependencies on PetState or combat logic.
/// </summary>
public static class OpponentDifficulty
{
    // ========================================
    // Tuning Constants
    // ========================================

    /// <summary>
    /// XP required per level in the square-root progression curve.
    /// Higher values make leveling slower.
    /// </summary>
    private const long XpPerLevel = 1000L;

    /// <summary>
    /// Base difficulty when opponents are evenly matched (same level, same proficiencies).
    /// </summary>
    private const float BaseDifficulty = 50f;

    /// <summary>
    /// Difficulty adjustment per level difference.
    /// Positive when opponent is higher level, negative when lower.
    /// </summary>
    private const float DifficultyPerLevel = 10f;

    /// <summary>
    /// Maximum difficulty contribution from weapon proficiency delta (±15).
    /// Applied when proficiency difference is ±100.
    /// </summary>
    private const float MaxWeaponProficiencyImpact = 15f;

    /// <summary>
    /// Maximum difficulty contribution from general proficiency delta (±10).
    /// Applied when proficiency difference is ±100.
    /// </summary>
    private const float MaxGeneralProficiencyImpact = 10f;

    /// <summary>
    /// Minimum difficulty value (prevents difficulty from going too low).
    /// </summary>
    private const float MinDifficulty = 10f;

    /// <summary>
    /// Maximum difficulty value (prevents difficulty from going too high).
    /// </summary>
    private const float MaxDifficulty = 100f;

    // ========================================
    // Public Methods
    // ========================================

    /// <summary>
    /// Computes the difficulty rating of an opponent based on level difference (simple version).
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
        float difficulty = BaseDifficulty + (levelDelta * DifficultyPerLevel);

        // Clamp result between 10 and 100
        return Math.Clamp(difficulty, MinDifficulty, MaxDifficulty);
    }

    /// <summary>
    /// Computes the difficulty rating of an opponent based on XP, weapon proficiency, and general proficiency.
    /// </summary>
    /// <param name="opponentXp">Experience points of the opponent.</param>
    /// <param name="playerXp">Experience points of the player/operator.</param>
    /// <param name="opponentWeaponProficiency">Opponent's weapon proficiency (0-100).</param>
    /// <param name="opponentGeneralProficiency">Opponent's general combat proficiency (0-100).</param>
    /// <param name="playerWeaponProficiency">Player's weapon proficiency (0-100).</param>
    /// <param name="playerGeneralProficiency">Player's general combat proficiency (0-100).</param>
    /// <returns>
    /// A difficulty rating between 10 and 100, where:
    /// - Base difficulty starts at 50 (evenly matched)
    /// - Level difference (from XP) contributes ±10 per level
    /// - Weapon proficiency difference contributes up to ±15
    /// - General proficiency difference contributes up to ±10
    /// - Result is clamped to the range [10, 100]
    /// </returns>
    /// <remarks>
    /// This method provides more nuanced difficulty calculation by considering:
    /// 1. Experience-based level differences (derived via square-root curve)
    /// 2. Weapon-specific skill differences
    /// 3. General combat capability differences
    /// 
    /// Proficiency modifiers are scaled linearly and have smaller impact than level differences,
    /// ensuring that experience remains the primary factor in difficulty assessment.
    /// 
    /// Examples:
    /// - Equal XP, equal proficiencies: Difficulty = 50
    /// - Opponent 9 levels higher (81k vs 0 XP): Difficulty increases by 90 (clamped to 100)
    /// - Opponent with +50 weapon prof, +50 general prof: Difficulty increases by ~12.5
    /// </remarks>
    public static float Compute(
        long opponentXp,
        long playerXp,
        float opponentWeaponProficiency,
        float opponentGeneralProficiency,
        float playerWeaponProficiency,
        float playerGeneralProficiency)
    {
        // Derive levels from XP using square-root curve
        int opponentLevel = ComputeLevelFromXp(opponentXp);
        int playerLevel = ComputeLevelFromXp(playerXp);

        // Calculate level difference contribution
        int levelDelta = opponentLevel - playerLevel;
        float difficulty = BaseDifficulty + (levelDelta * DifficultyPerLevel);

        // Calculate weapon proficiency contribution
        // Delta in range [-100, +100], scaled to max impact of ±15
        float weaponProfDelta = opponentWeaponProficiency - playerWeaponProficiency;
        float weaponImpact = (weaponProfDelta / 100f) * MaxWeaponProficiencyImpact;
        difficulty += weaponImpact;

        // Calculate general proficiency contribution
        // Delta in range [-100, +100], scaled to max impact of ±10
        float generalProfDelta = opponentGeneralProficiency - playerGeneralProficiency;
        float generalImpact = (generalProfDelta / 100f) * MaxGeneralProficiencyImpact;
        difficulty += generalImpact;

        // Clamp final result to valid range
        return Math.Clamp(difficulty, MinDifficulty, MaxDifficulty);
    }

    /// <summary>
    /// Computes the level from experience points using a square-root progression curve.
    /// </summary>
    /// <param name="xp">Experience points (must be non-negative).</param>
    /// <returns>
    /// The level derived from XP, where:
    /// - Level 0 requires 0 XP
    /// - Level 1 requires 1,000 XP
    /// - Level 2 requires 4,000 XP
    /// - Level 3 requires 9,000 XP
    /// - Level increases with the square root of XP
    /// - Result is always >= 0
    /// </returns>
    /// <remarks>
    /// Uses the formula: Level = floor(sqrt(xp / XpPerLevel))
    /// 
    /// This creates a progression where:
    /// - Early levels are relatively quick to achieve
    /// - Later levels require increasingly more XP
    /// - The curve is smooth and continuous
    /// 
    /// Example progression with XpPerLevel = 1000:
    /// - 0 XP → Level 0
    /// - 1,000 XP → Level 1
    /// - 4,000 XP → Level 2
    /// - 9,000 XP → Level 3
    /// - 16,000 XP → Level 4
    /// - 25,000 XP → Level 5
    /// </remarks>
    public static int ComputeLevelFromXp(long xp)
    {
        // Handle negative XP gracefully (shouldn't happen, but be defensive)
        if (xp < 0)
        {
            return 0;
        }

        // Apply square-root curve: Level = floor(sqrt(xp / XpPerLevel))
        double scaledXp = (double)xp / XpPerLevel;
        int level = (int)Math.Floor(Math.Sqrt(scaledXp));

        // Ensure level is non-negative
        return Math.Max(0, level);
    }
}
