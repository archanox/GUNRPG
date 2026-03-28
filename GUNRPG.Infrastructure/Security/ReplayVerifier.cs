using System.Security.Cryptography;
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
///   <item>Guard: tick chain must be strictly ordered by tick index.</item>
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
    /// Must be strictly ordered by <see cref="SignedTick.Tick"/>; returns
    /// <see langword="false"/> if the invariant is violated.
    /// </param>
    /// <param name="runResult">
    /// The signed run result to verify, including checkpoints and Merkle root.
    /// </param>
    /// <param name="simulation">
    /// A deterministic simulation implementation used to re-execute the tick chain
    /// and compare state hashes against the signed checkpoints.
    /// <see cref="IDeterministicSimulation.GetStateHash"/> must return exactly
    /// <see cref="SHA256.HashSizeInBytes"/> (32) bytes; an invalid-length hash is
    /// treated as a divergence and causes the method to return <see langword="false"/>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> if every verification step passes;
    /// <see langword="false"/> if any check fails (out-of-order tick chain, invalid
    /// signature, Merkle mismatch, checkpoint structure violation, invalid state hash
    /// length, or simulation state divergence).
    /// </returns>
    public bool VerifyRun(
        IReadOnlyList<SignedTick> tickChain,
        SignedRunResult runResult,
        IDeterministicSimulation simulation)
    {
        ArgumentNullException.ThrowIfNull(tickChain);
        ArgumentNullException.ThrowIfNull(runResult);
        ArgumentNullException.ThrowIfNull(simulation);

        // Guard: tick chain must be strictly ordered by tick index.
        for (var i = 1; i < tickChain.Count; i++)
        {
            if (tickChain[i].Tick <= tickChain[i - 1].Tick)
                return false;
        }

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

        // Build a O(1) lookup from tick value to position in tickChain.
        // This prevents O(n) chain scans on each binary-search replay step.
        var tickPositionLookup = BuildTickPositionLookup(tickChain);

        // Step 4: Binary-search checkpoint simulation.
        // Verify the first checkpoint by replaying from genesis.
        simulation.Reset();
        ReplayUpTo(simulation, tickChain, tickPositionLookup[checkpoints[0].TickIndex]);
        var firstHash = simulation.GetStateHash();
        if (!IsValidStateHash(firstHash) || !firstHash.AsSpan().SequenceEqual(checkpoints[0].StateHash))
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
            ReplayUpTo(simulation, tickChain, tickPositionLookup[checkpoints[mid].TickIndex]);

            var midHash = simulation.GetStateHash();
            if (IsValidStateHash(midHash) && midHash.AsSpan().SequenceEqual(checkpoints[mid].StateHash))
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
        var lowEndIndex = tickPositionLookup[checkpoints[low].TickIndex];
        ReplayUpTo(simulation, tickChain, lowEndIndex);

        var highEndIndex = tickPositionLookup[checkpoints[high].TickIndex];
        for (var i = lowEndIndex + 1; i <= highEndIndex; i++)
            simulation.ApplyTick(tickChain[i]);

        var highHash = simulation.GetStateHash();
        return IsValidStateHash(highHash) && highHash.AsSpan().SequenceEqual(checkpoints[high].StateHash);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Private helpers
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="hash"/> is a valid
    /// SHA-256 hash: non-null and exactly <see cref="SHA256.HashSizeInBytes"/> bytes.
    /// </summary>
    private static bool IsValidStateHash(byte[]? hash) =>
        hash is not null && hash.Length == SHA256.HashSizeInBytes;

    /// <summary>
    /// Builds a dictionary that maps each <see cref="SignedTick.Tick"/> value to its
    /// zero-based index in <paramref name="tickChain"/>, enabling O(1) position lookups.
    /// </summary>
    private static Dictionary<long, int> BuildTickPositionLookup(IReadOnlyList<SignedTick> tickChain)
    {
        var lookup = new Dictionary<long, int>(tickChain.Count);
        for (var i = 0; i < tickChain.Count; i++)
            lookup[tickChain[i].Tick] = i;
        return lookup;
    }

    /// <summary>
    /// Applies ticks from the chain at positions 0 through
    /// <paramref name="endIndexInclusive"/> (inclusive) to <paramref name="simulation"/>.
    /// The caller is responsible for calling <see cref="IDeterministicSimulation.Reset"/>
    /// before this method when a fresh replay is required.
    /// </summary>
    private static void ReplayUpTo(
        IDeterministicSimulation simulation,
        IReadOnlyList<SignedTick> tickChain,
        int endIndexInclusive)
    {
        for (var i = 0; i <= endIndexInclusive; i++)
            simulation.ApplyTick(tickChain[i]);
    }

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
        return IsValidStateHash(simulatedHash) && simulatedHash.AsSpan().SequenceEqual(finalHash);
    }
}
