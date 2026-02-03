namespace GUNRPG.Core.VirtualPet;

/// <summary>
/// Pure functional rules engine for applying state transitions to PetState.
/// Contains no mutable state and performs deterministic transformations.
/// </summary>
public static class PetRules
{
    /// <summary>
    /// Applies background decay and a PetInput action to produce a new PetState.
    /// This is a pure function with no side effects or mutable state.
    /// </summary>
    /// <param name="state">The current pet state.</param>
    /// <param name="input">The input action to apply.</param>
    /// <param name="now">The current time for calculating elapsed time.</param>
    /// <returns>A new PetState with applied changes.</returns>
    public static PetState Apply(PetState state, PetInput input, DateTimeOffset now)
    {
        // Calculate time elapsed since last update
        TimeSpan elapsed = now - state.LastUpdated;

        // Apply background decay based on elapsed time
        var decayedState = ApplyBackgroundDecay(state, elapsed);

        // Apply the specific input action
        var finalState = input switch
        {
            RestInput rest => ApplyRest(decayedState, rest),
            EatInput eat => ApplyEat(decayedState, eat),
            DrinkInput drink => ApplyDrink(decayedState, drink),
            MissionInput mission => ApplyMission(decayedState, mission),
            _ => decayedState
        };

        // Update the timestamp and return
        return finalState with { LastUpdated = now };
    }

    /// <summary>
    /// Applies background decay to all stats based on elapsed time.
    /// </summary>
    private static PetState ApplyBackgroundDecay(PetState state, TimeSpan elapsed)
    {
        float hours = (float)elapsed.TotalHours;

        // Calculate decay amounts
        float hungerIncrease = PetConstants.HungerIncreasePerHour * hours;
        float hydrationDecrease = PetConstants.HydrationDecreasePerHour * hours;
        float fatigueIncrease = PetConstants.FatigueIncreasePerHour * hours;
        float stressIncrease = PetConstants.StressIncreasePerHour * hours;

        // Apply decay and clamp to valid ranges
        return state with
        {
            Hunger = Clamp(state.Hunger + hungerIncrease),
            Hydration = Clamp(state.Hydration - hydrationDecrease),
            Fatigue = Clamp(state.Fatigue + fatigueIncrease),
            Stress = Clamp(state.Stress + stressIncrease)
        };
    }

    /// <summary>
    /// Applies rest action to the pet state.
    /// Rest reduces fatigue and stress, and recovers health.
    /// </summary>
    private static PetState ApplyRest(PetState state, RestInput input)
    {
        float hours = (float)input.Duration.TotalHours;

        // Calculate recovery amounts
        float healthRecovery = PetConstants.HealthRecoveryPerHour * hours;
        float fatigueRecovery = PetConstants.FatigueRecoveryPerHour * hours;
        float stressRecovery = PetConstants.StressRecoveryPerHour * hours;

        // Apply recovery and clamp to valid ranges
        return state with
        {
            Health = Clamp(state.Health + healthRecovery),
            Fatigue = Clamp(state.Fatigue - fatigueRecovery),
            Stress = Clamp(state.Stress - stressRecovery)
        };
    }

    /// <summary>
    /// Applies eating action to the pet state.
    /// Eating reduces hunger.
    /// </summary>
    private static PetState ApplyEat(PetState state, EatInput input)
    {
        // Reduce hunger by nutrition value (assuming lower hunger is better)
        return state with
        {
            Hunger = Clamp(state.Hunger - input.Nutrition)
        };
    }

    /// <summary>
    /// Applies drinking action to the pet state.
    /// Drinking increases hydration.
    /// </summary>
    private static PetState ApplyDrink(PetState state, DrinkInput input)
    {
        // Increase hydration
        return state with
        {
            Hydration = Clamp(state.Hydration + input.Hydration)
        };
    }

    /// <summary>
    /// Applies mission action to the pet state.
    /// Missions increase stress and may cause injury.
    /// </summary>
    private static PetState ApplyMission(PetState state, MissionInput input)
    {
        // Apply mission effects
        return state with
        {
            Stress = Clamp(state.Stress + input.StressLoad),
            Injury = Clamp(state.Injury + input.InjuryRisk)
        };
    }

    /// <summary>
    /// Clamps a stat value to the valid range defined by PetConstants.
    /// </summary>
    private static float Clamp(float value)
    {
        return Math.Clamp(value, PetConstants.MinStatValue, PetConstants.MaxStatValue);
    }
}
