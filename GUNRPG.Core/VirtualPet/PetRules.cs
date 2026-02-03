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
    /// Implements conditional coupling rules where certain stats affect others.
    /// </summary>
    private static PetState ApplyBackgroundDecay(PetState state, TimeSpan elapsed)
    {
        float hours = (float)elapsed.TotalHours;

        // Base decay amounts (always applied)
        float hungerIncrease = PetConstants.HungerIncreasePerHour * hours;
        float hydrationDecrease = PetConstants.HydrationDecreasePerHour * hours;

        // Fatigue increases at base rate, faster when stress is high
        float fatigueIncrease = PetConstants.FatigueIncreasePerHour * hours;
        if (state.Stress > PetConstants.HighStressThreshold)
        {
            fatigueIncrease += PetConstants.FatigueIncreaseHighStress * hours;
        }

        // Stress increases at base rate, faster when injury is high
        float stressIncrease = PetConstants.StressIncreasePerHour * hours;
        if (state.Injury > PetConstants.HighInjuryThreshold)
        {
            stressIncrease += PetConstants.StressIncreaseHighInjury * hours;
        }

        // Morale decreases slowly when stress is elevated
        float moraleDecrease = 0f;
        if (state.Stress > PetConstants.MoraleDecayStressThreshold)
        {
            moraleDecrease = PetConstants.MoraleDecayPerHour * hours;
        }

        // Health decay is conditional on critical conditions
        float healthDecrease = 0f;
        bool healthDecayActive = state.Hunger > PetConstants.CriticalHungerThreshold
                               || state.Hydration > PetConstants.CriticalHydrationThreshold
                               || state.Injury > PetConstants.CriticalInjuryThreshold;

        if (healthDecayActive)
        {
            healthDecrease = PetConstants.HealthDecayPerHour * hours;
            // Morale decreases faster while health is decaying
            moraleDecrease += PetConstants.MoraleDecayDuringHealthDecay * hours;
        }

        // Calculate new stat values (unclamped)
        float newHunger = state.Hunger + hungerIncrease;
        float newHydration = state.Hydration - hydrationDecrease;
        float newFatigue = state.Fatigue + fatigueIncrease;
        float newStress = state.Stress + stressIncrease;
        float newMorale = state.Morale - moraleDecrease;
        float newHealth = state.Health - healthDecrease;

        // Apply all changes and clamp once at the end
        return state with
        {
            Hunger = Clamp(newHunger),
            Hydration = Clamp(newHydration),
            Fatigue = Clamp(newFatigue),
            Stress = Clamp(newStress),
            Morale = Clamp(newMorale),
            Health = Clamp(newHealth)
        };
    }

    /// <summary>
    /// Applies rest action to the pet state.
    /// Rest reduces fatigue and stress, and recovers health.
    /// Recovery rates are proportionally reduced based on adverse conditions.
    /// </summary>
    private static PetState ApplyRest(PetState state, RestInput input)
    {
        float hours = (float)input.Duration.TotalHours;

        // Calculate base recovery amounts
        float healthRecovery = PetConstants.HealthRecoveryPerHour * hours;
        float fatigueRecovery = PetConstants.FatigueRecoveryPerHour * hours;
        float stressRecovery = PetConstants.StressRecoveryPerHour * hours;

        // Apply recovery reduction multipliers based on current state
        
        // Health recovery is reduced when injured
        if (state.Injury > PetConstants.InjuryRecoveryReductionThreshold)
        {
            float injuryFactor = (state.Injury - PetConstants.InjuryRecoveryReductionThreshold) 
                               / (PetConstants.MaxStatValue - PetConstants.InjuryRecoveryReductionThreshold);
            float healthMultiplier = 1f - (injuryFactor * (1f - PetConstants.MinRecoveryMultiplier));
            healthRecovery *= healthMultiplier;
        }

        // Stress recovery is reduced when hungry or dehydrated
        float stressMultiplier = 1f;
        if (state.Hunger > PetConstants.HungerStressRecoveryThreshold)
        {
            float hungerFactor = (state.Hunger - PetConstants.HungerStressRecoveryThreshold) 
                               / (PetConstants.MaxStatValue - PetConstants.HungerStressRecoveryThreshold);
            stressMultiplier = Math.Min(stressMultiplier, 1f - (hungerFactor * (1f - PetConstants.MinRecoveryMultiplier)));
        }
        if (state.Hydration > PetConstants.HydrationStressRecoveryThreshold)
        {
            float hydrationFactor = (state.Hydration - PetConstants.HydrationStressRecoveryThreshold) 
                                  / (PetConstants.MaxStatValue - PetConstants.HydrationStressRecoveryThreshold);
            stressMultiplier = Math.Min(stressMultiplier, 1f - (hydrationFactor * (1f - PetConstants.MinRecoveryMultiplier)));
        }
        stressRecovery *= stressMultiplier;

        // Fatigue recovery is reduced when stressed
        if (state.Stress > PetConstants.StressFatigueRecoveryThreshold)
        {
            float stressFactor = (state.Stress - PetConstants.StressFatigueRecoveryThreshold) 
                               / (PetConstants.MaxStatValue - PetConstants.StressFatigueRecoveryThreshold);
            float fatigueMultiplier = 1f - (stressFactor * (1f - PetConstants.MinRecoveryMultiplier));
            fatigueRecovery *= fatigueMultiplier;
        }

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
