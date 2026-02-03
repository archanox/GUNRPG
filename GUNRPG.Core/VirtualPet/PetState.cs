namespace GUNRPG.Core.VirtualPet;

/// <summary>
/// Immutable record representing the condition of a virtual operator.
/// Pure data model with no behavior or validation.
/// </summary>
/// <param name="OperatorId">Unique identifier for the operator.</param>
/// <param name="Health">Physical health level. Expected range: 0-100.</param>
/// <param name="Fatigue">Fatigue level. Expected range: 0-100.</param>
/// <param name="Injury">Injury severity. Expected range: 0-100.</param>
/// <param name="Stress">Mental stress level. Expected range: 0-100.</param>
/// <param name="Morale">Morale level. Expected range: 0-100.</param>
/// <param name="Hunger">Hunger level. Expected range: 0-100.</param>
/// <param name="Hydration">Hydration level. Expected range: 0-100.</param>
/// <param name="LastUpdated">Timestamp of the last state update.</param>
public sealed record PetState(
    Guid OperatorId,
    float Health,
    float Fatigue,
    float Injury,
    float Stress,
    float Morale,
    float Hunger,
    float Hydration,
    DateTimeOffset LastUpdated
);
