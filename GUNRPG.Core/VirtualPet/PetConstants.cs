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
}
