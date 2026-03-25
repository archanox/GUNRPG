namespace GUNRPG.Core.Simulation;

/// <summary>
/// Identifies the role of a node in the distributed simulation network.
/// </summary>
public enum NodeRole
{
    /// <summary>
    /// The authority node. Signs ticks and produces the canonical signed state hash chain.
    /// </summary>
    Authority,

    /// <summary>
    /// A validator node. Simulates locally and verifies every received <see cref="SignedTick"/>
    /// against its own state, detecting desyncs and invalid signatures.
    /// </summary>
    Validator,

    /// <summary>
    /// A client node. Submits player inputs and receives signed state updates from the authority.
    /// </summary>
    Client,
}
