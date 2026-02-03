namespace GUNRPG.Core.VirtualPet;

/// <summary>
/// Immutable record representing the condition of a virtual operator.
/// Pure data model with no behavior.
/// </summary>
public sealed record PetState(
    // Identity
    Guid OperatorId,
    
    // Physical (0-100)
    float Health,
    float Fatigue,
    float Injury,
    
    // Mental (0-100)
    float Stress,
    float Morale,
    
    // Care (0-100)
    float Hunger,
    float Hydration,
    
    // Timestamp
    DateTimeOffset LastUpdated
);
