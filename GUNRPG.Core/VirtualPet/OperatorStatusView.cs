namespace GUNRPG.Core.VirtualPet;

/// <summary>
/// Read-only presentation-layer projection of operator condition.
/// This model exposes operator state in a form suitable for UI, debugging, and server-client transfer.
/// It is derived from PetState and contains no behavior.
/// </summary>
/// <param name="Health">Physical health level. Expected range: 0-100.</param>
/// <param name="Injury">Injury severity. Expected range: 0-100.</param>
/// <param name="Fatigue">Fatigue level. Expected range: 0-100.</param>
/// <param name="Stress">Mental stress level. Expected range: 0-100.</param>
/// <param name="Morale">Morale level. Expected range: 0-100.</param>
/// <param name="Hunger">Hunger level. Expected range: 0-100.</param>
/// <param name="Hydration">Hydration level. Expected range: 0-100.</param>
/// <param name="CombatReadiness">Optional derived metric for combat effectiveness. Null if not computed.</param>
public sealed record OperatorStatusView(
    float Health,
    float Injury,
    float Fatigue,
    float Stress,
    float Morale,
    float Hunger,
    float Hydration,
    float? CombatReadiness
)
{
    /// <summary>
    /// Creates an OperatorStatusView from a PetState.
    /// This is a pure, deterministic function that performs a simple projection.
    /// </summary>
    /// <param name="state">The source PetState to project.</param>
    /// <returns>A new OperatorStatusView containing the operator's condition.</returns>
    public static OperatorStatusView From(PetState state)
    {
        return new OperatorStatusView(
            Health: state.Health,
            Injury: state.Injury,
            Fatigue: state.Fatigue,
            Stress: state.Stress,
            Morale: state.Morale,
            Hunger: state.Hunger,
            Hydration: state.Hydration,
            CombatReadiness: null  // No helper available; easily removable
        );
    }
}
