namespace GUNRPG.Core.VirtualPet;

/// <summary>
/// Tunable constants for the virtual pet simulation system.
/// These values are provisional and should be adjusted based on gameplay testing.
/// </summary>
public static class PetConstants
{
    /// <summary>
    /// Maximum value for any stat (0-100 range).
    /// </summary>
    public const float MaxStatValue = 100f;

    /// <summary>
    /// Minimum value for any stat (0-100 range).
    /// </summary>
    public const float MinStatValue = 0f;

    // ========================================
    // Background Decay Rates (per hour)
    // ========================================

    /// <summary>
    /// Rate at which hunger increases per hour when not eating.
    /// Provisional value - adjust based on gameplay testing.
    /// </summary>
    public const float HungerIncreasePerHour = 5f;

    /// <summary>
    /// Rate at which hydration decreases per hour when not drinking.
    /// Provisional value - adjust based on gameplay testing.
    /// </summary>
    public const float HydrationDecreasePerHour = 8f;

    /// <summary>
    /// Rate at which fatigue increases per hour during activity.
    /// Provisional value - adjust based on gameplay testing.
    /// </summary>
    public const float FatigueIncreasePerHour = 10f;

    /// <summary>
    /// Rate at which stress increases per hour under normal conditions.
    /// Provisional value - adjust based on gameplay testing.
    /// </summary>
    public const float StressIncreasePerHour = 3f;

    // ========================================
    // Recovery Rates (per hour)
    // ========================================

    /// <summary>
    /// Rate at which health recovers per hour during rest.
    /// Provisional value - adjust based on gameplay testing.
    /// </summary>
    public const float HealthRecoveryPerHour = 15f;

    /// <summary>
    /// Rate at which stress decreases per hour during rest/relaxation.
    /// Provisional value - adjust based on gameplay testing.
    /// </summary>
    public const float StressRecoveryPerHour = 12f;

    /// <summary>
    /// Rate at which fatigue decreases per hour during rest.
    /// Provisional value - adjust based on gameplay testing.
    /// </summary>
    public const float FatigueRecoveryPerHour = 20f;

    // ========================================
    // Conditional Decay Thresholds
    // ========================================

    /// <summary>
    /// Stress threshold above which fatigue increases faster.
    /// Provisional value - adjust based on gameplay testing.
    /// </summary>
    public const float HighStressThreshold = 60f;

    /// <summary>
    /// Injury threshold above which stress increases faster.
    /// Provisional value - adjust based on gameplay testing.
    /// </summary>
    public const float HighInjuryThreshold = 40f;

    /// <summary>
    /// Stress threshold above which morale starts to decrease.
    /// Provisional value - adjust based on gameplay testing.
    /// </summary>
    public const float MoraleDecayStressThreshold = 50f;

    /// <summary>
    /// Hunger threshold above which health decay becomes active.
    /// Provisional value - adjust based on gameplay testing.
    /// </summary>
    public const float CriticalHungerThreshold = 80f;

    /// <summary>
    /// Hydration threshold above which health decay becomes active.
    /// Provisional value - adjust based on gameplay testing.
    /// </summary>
    public const float CriticalHydrationThreshold = 80f;

    /// <summary>
    /// Injury threshold above which health decay becomes active.
    /// Provisional value - adjust based on gameplay testing.
    /// </summary>
    public const float CriticalInjuryThreshold = 60f;

    // ========================================
    // Conditional Decay Rates (per hour)
    // ========================================

    /// <summary>
    /// Additional fatigue increase per hour when stress is high.
    /// Provisional value - adjust based on gameplay testing.
    /// </summary>
    public const float FatigueIncreaseHighStress = 5f;

    /// <summary>
    /// Additional stress increase per hour when injury is high.
    /// Provisional value - adjust based on gameplay testing.
    /// </summary>
    public const float StressIncreaseHighInjury = 4f;

    /// <summary>
    /// Morale decrease per hour when stress is elevated.
    /// Provisional value - adjust based on gameplay testing.
    /// </summary>
    public const float MoraleDecayPerHour = 2f;

    /// <summary>
    /// Health decrease per hour when critical conditions are met.
    /// Provisional value - adjust based on gameplay testing.
    /// </summary>
    public const float HealthDecayPerHour = 3f;

    /// <summary>
    /// Additional morale decrease per hour when health is decaying.
    /// Provisional value - adjust based on gameplay testing.
    /// </summary>
    public const float MoraleDecayDuringHealthDecay = 3f;
}
