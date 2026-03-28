using System.Security.Cryptography;
using GUNRPG.Core.Simulation;

namespace GUNRPG.Security;

/// <summary>
/// Verifies a completed signed run using binary-search over checkpoints combined
/// with deterministic replay of tick segments.
/// </summary>
/// <remarks>
/// This strategy trades a single linear replay for multiple shorter replays:
/// it can localise a divergence point in O(log k) checkpoint probes (where k is
/// the number of checkpoints) and exit early once a mismatch is found, but in
/// the worst case may perform O(n log k) tick applications and therefore can
/// require more total simulation work than one O(n) linear replay for valid runs.
/// <para>Full verification procedure:</para>
/// <list type="number">
///   <item>Guard: tick chain must be non-null, contain no null entries, and be strictly ordered by tick index.</item>
///   <item>Verify the run signature (<see cref="SessionAuthority.VerifySignedRun"/>).</item>
///   <item>Verify the Merkle root (<see cref="TickAuthorityService.VerifyTickMerkleRoot"/>).</item>
///   <item>Verify checkpoint structure (<see cref="TickAuthorityService.VerifyCheckpoints"/>).</item>
///   <item>Binary-search checkpoint simulation: probe midpoints between checkpoints to locate divergence.</item>
///   <item>Linear replay within the narrowed divergence window to confirm the final segment.</item>
///   <item>Confirm the signed final hash (<see cref="SignedRunResult.FinalHash"/>) matches the simulated end state.</item>
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

        // Guard: tick chain must be strictly ordered by tick index and contain no null entries.
        for (var i = 0; i < tickChain.Count; i++)
        {
            if (tickChain[i] is null)
                return false;

            if (i > 0 && tickChain[i].Tick <= tickChain[i - 1].Tick)
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

        // Defensively verify that every checkpoint tick index is present in the lookup.
        // VerifyCheckpoints (step 3) enforces this invariant, but we guard here to prevent
        // KeyNotFoundException on any subsequent lookup access.
        foreach (var cp in checkpoints)
        {
            if (!tickPositionLookup.ContainsKey(cp.TickIndex))
                return false;
        }

        // Step 4: Binary-search checkpoint simulation.
        // Verify the first checkpoint by replaying from genesis.
        simulation.Reset();
        ReplayUpTo(simulation, tickChain, tickPositionLookup[checkpoints[0].TickIndex]);
        var firstHash = simulation.GetStateHash();
        if (!IsValidStateHash(firstHash) || !firstHash.AsSpan().SequenceEqual(checkpoints[0].StateHash))
            return false;

        // With a single checkpoint, ensure it corresponds to the last tick in the chain
        // and that the simulated hash matches the signed final hash.
        if (checkpoints.Count == 1)
        {
            if (checkpoints[0].TickIndex != tickChain[^1].Tick)
                return false;

            return TryParseHexHash(runResult.FinalHash, out var singleFinalHash)
                   && firstHash.AsSpan().SequenceEqual(singleFinalHash);
        }

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
        if (!IsValidStateHash(highHash) || !highHash.AsSpan().SequenceEqual(checkpoints[high].StateHash))
            return false;

        // Ensure the last checkpoint covers the final tick in the chain, and that the
        // signed final hash matches the simulated state — so FinalHash is always validated
        // even when the checkpoint list might omit the final tick.
        // Safety: checkpoints.Count >= 2 here (Count 0 and Count 1 both returned early),
        // and tickChain is non-empty (VerifyCheckpoints passed with non-empty checkpoints).
        if (checkpoints[^1].TickIndex != tickChain[^1].Tick)
            return false;

        return TryParseHexHash(runResult.FinalHash, out var signedFinalHash)
               && highHash.AsSpan().SequenceEqual(signedFinalHash);
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

        var simulatedHash = simulation.GetStateHash();
        return TryParseHexHash(finalHashHex, out var finalHash)
               && IsValidStateHash(simulatedHash)
               && simulatedHash.AsSpan().SequenceEqual(finalHash);
    }

    /// <summary>
    /// Attempts to decode <paramref name="hexHash"/> as a hex-encoded byte array.
    /// Returns <see langword="false"/> and sets <paramref name="hash"/> to an empty array
    /// if decoding fails.
    /// </summary>
    private static bool TryParseHexHash(string hexHash, out byte[] hash)
    {
        try
        {
            hash = Convert.FromHexString(hexHash);
            return true;
        }
        catch (FormatException)
        {
            hash = [];
            return false;
        }
    }
}
