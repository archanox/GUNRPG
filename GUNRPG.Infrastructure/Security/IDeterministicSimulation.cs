using GUNRPG.Core.Simulation;

namespace GUNRPG.Security;

/// <summary>
/// Abstraction for a deterministic simulation that can be reset and replayed
/// to an arbitrary tick, producing a verifiable state hash at each step.
/// </summary>
/// <remarks>
/// Implementations must be fully deterministic: identical tick sequences must
/// always produce identical state hashes across all nodes.
/// Used by <see cref="ReplayVerifier"/> to re-execute tick chains and compare
/// state hashes against the signed checkpoints in a <see cref="SignedRunResult"/>.
/// </remarks>
public interface IDeterministicSimulation
{
    /// <summary>
    /// Resets the simulation to its initial (genesis) state.
    /// After this call, <see cref="GetStateHash"/> reflects the state before any ticks.
    /// </summary>
    void Reset();

    /// <summary>
    /// Applies the given signed tick to advance the simulation by one step.
    /// After this call, <see cref="GetStateHash"/> reflects the simulation state
    /// produced by processing <paramref name="tick"/>.
    /// </summary>
    /// <param name="tick">The signed tick whose inputs drive this simulation step.</param>
    void ApplyTick(SignedTick tick);

    /// <summary>
    /// Returns the SHA-256 hash of the current simulation state (32 bytes).
    /// </summary>
    byte[] GetStateHash();
}
