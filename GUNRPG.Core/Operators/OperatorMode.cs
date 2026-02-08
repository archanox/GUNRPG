namespace GUNRPG.Core.Operators;

/// <summary>
/// Represents the operational mode of an operator.
/// Determines which actions are allowed and enforces the boundary between base management and field operations.
/// </summary>
public enum OperatorMode
{
    /// <summary>
    /// Operator is at base. Can modify loadout, treat wounds, and prepare for missions.
    /// Cannot engage in combat or scavenge loot.
    /// </summary>
    Base,
    
    /// <summary>
    /// Operator is deployed in the field (infiltrated). Can engage in combat and scavenge.
    /// Cannot modify loadout or perform base-only maintenance.
    /// Must exfil within 30 minutes or face failure consequences.
    /// </summary>
    Infil
}
