using GUNRPG.Core.Simulation;

namespace GUNRPG.Security;

/// <summary>
/// Verifies a completed signed run using binary-search checkpoint simulation,
/// reducing replay complexity from O(n) sequential steps to O(n log k) where
/// k is the number of checkpoints.
/// </summary>
/// <remarks>
/// Full verification procedure:
/// <list type="number">
///   <item>Verify the run signature (<see cref="SessionAuthority.VerifySignedRun"/>).</item>
///   <item>Verify the Merkle root (<see cref="TickAuthorityService.VerifyTickMerkleRoot"/>).</item>
///   <item>Verify checkpoint structure (<see cref="TickAuthorityService.VerifyCheckpoints"/>).</item>
///   <item>Binary-search checkpoint simulation: locate divergence in O(log k) replay segments.</item>
///   <item>Linear replay within the divergence window to confirm the final segment.</item>
///   <item>Confirm the final state hash (the last checkpoint is always the final tick).</item>
/// </list>
/// If any step fails the method returns <see langword="false"/> immediately (early exit).
/// </remarks>
public sealed class ReplayVerifier
{
    private readonly Authority _authority;

    /// <summary>
    /// Initialises a new <see cref="ReplayVerifier"/> that verifies runs signed by
    /// <paramref name="authority"/>.
    /// </summary>
    /// <param name="authority">The authority whose public key is used to verify signatures.</param>
    public ReplayVerifier(Authority authority)
    {
        ArgumentNullException.ThrowIfNull(authority);
        _authority = authority;
    }

    /// <summary>
    /// Performs full verification of a signed run against a deterministic simulation.
    /// </summary>
    /// <param name="tickChain">
    /// The complete, ordered chain of signed tick checkpoints for the run.
    /// </param>
    /// <param name="runResult">
    /// The signed run result to verify, including checkpoints and Merkle root.
    /// </param>
    /// <param name="simulation">
    /// A deterministic simulation implementation used to re-execute the tick chain
    /// and compare state hashes against the signed checkpoints.
    /// </param>
    /// <returns>
    /// <see langword="true"/> if every verification step passes;
    /// <see langword="false"/> if any check fails (invalid signature, Merkle mismatch,
    /// checkpoint structure violation, or simulation state divergence).
    /// </returns>
    public bool VerifyRun(
        IReadOnlyList<SignedTick> tickChain,
        SignedRunResult runResult,
        IDeterministicSimulation simulation)
    {
        ArgumentNullException.ThrowIfNull(tickChain);
        ArgumentNullException.ThrowIfNull(runResult);
        ArgumentNullException.ThrowIfNull(simulation);

        // Step 1: Verify the run signature.
        if (!SessionAuthority.VerifySignedRun(runResult, _authority))
            return false;

        // Step 2: Verify the Merkle root of the tick chain.
        if (!TickAuthorityService.VerifyTickMerkleRoot(runResult, tickChain))
            return false;

        // Step 3: Verify checkpoint structure (fast two-pointer scan).
        if (!TickAuthorityService.VerifyCheckpoints(runResult, tickChain))
            return false;

        var checkpoints = runResult.Checkpoints;

        // No checkpoints: fall back to a direct full replay to confirm the final state hash.
        if (checkpoints is null || checkpoints.Count == 0)
            return VerifyFinalHashByReplay(simulation, tickChain, runResult.FinalHash);

        // Step 4: Binary-search checkpoint simulation.
        // Verify the first checkpoint by replaying from genesis.
        simulation.Reset();
        ReplayUpTo(simulation, tickChain, checkpoints[0].TickIndex);
        var firstHash = simulation.GetStateHash();
        if (!firstHash.AsSpan().SequenceEqual(checkpoints[0].StateHash))
            return false;

        // With a single checkpoint (= final tick), verification is already complete.
        if (checkpoints.Count == 1)
            return true;

        var low = 0;
        var high = checkpoints.Count - 1;

        // Binary search: narrow down to the smallest window [low, high] that may
        // contain a divergence.  low is always the last verified-good checkpoint index;
        // high is always the first unverified (or known-bad) checkpoint index.
        while (high - low > 1)
        {
            var mid = low + (high - low) / 2;

            simulation.Reset();
            ReplayUpTo(simulation, tickChain, checkpoints[mid].TickIndex);

            var midHash = simulation.GetStateHash();
            if (midHash.AsSpan().SequenceEqual(checkpoints[mid].StateHash))
                low = mid;
            else
                high = mid;
        }

        // Step 5 & 6: Linear replay within the [low, high] window.
        // Re-simulate from the last verified checkpoint (low) and apply ticks one-by-one
        // up to the candidate divergence checkpoint (high).
        // Because the last checkpoint in the result is always the final tick, this step
        // also serves as the final-state-hash confirmation (step 6).
        simulation.Reset();
        ReplayUpTo(simulation, tickChain, checkpoints[low].TickIndex);

        foreach (var tick in TicksAfter(tickChain, checkpoints[low].TickIndex, checkpoints[high].TickIndex))
            simulation.ApplyTick(tick);

        var highHash = simulation.GetStateHash();
        return highHash.AsSpan().SequenceEqual(checkpoints[high].StateHash);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Private helpers
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Full replay fallback when the run result has no checkpoints.
    /// Replays every tick and compares the final state hash against
    /// <paramref name="finalHashHex"/>.
    /// </summary>
    private static bool VerifyFinalHashByReplay(
        IDeterministicSimulation simulation,
        IReadOnlyList<SignedTick> tickChain,
        string finalHashHex)
    {
        simulation.Reset();
        foreach (var tick in tickChain)
            simulation.ApplyTick(tick);

        byte[] finalHash;
        try { finalHash = Convert.FromHexString(finalHashHex); }
        catch (FormatException) { return false; }

        var simulatedHash = simulation.GetStateHash();
        return simulatedHash.AsSpan().SequenceEqual(finalHash);
    }

    /// <summary>
    /// Resets the simulation and replays all ticks with
    /// <see cref="SignedTick.Tick"/> &lt;= <paramref name="targetTickIndex"/>.
    /// </summary>
    private static void ReplayUpTo(
        IDeterministicSimulation simulation,
        IReadOnlyList<SignedTick> tickChain,
        long targetTickIndex)
    {
        foreach (var tick in tickChain)
        {
            if (tick.Tick > targetTickIndex)
                break;
            simulation.ApplyTick(tick);
        }
    }

    /// <summary>
    /// Returns ticks in <paramref name="tickChain"/> with tick index strictly greater than
    /// <paramref name="afterTickIndex"/> and at most <paramref name="upToTickIndex"/>.
    /// Assumes the chain is ordered by ascending tick index.
    /// </summary>
    private static IEnumerable<SignedTick> TicksAfter(
        IReadOnlyList<SignedTick> tickChain,
        long afterTickIndex,
        long upToTickIndex)
    {
        foreach (var tick in tickChain)
        {
            if (tick.Tick <= afterTickIndex) continue;
            if (tick.Tick > upToTickIndex) break;
            yield return tick;
        }
    }
}
