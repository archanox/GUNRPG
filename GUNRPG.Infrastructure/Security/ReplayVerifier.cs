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
/// When optional <see cref="StateSnapshot"/>s are provided to <see cref="VerifyRun"/>,
/// each binary-search probe is accelerated by starting from the nearest trusted snapshot
/// instead of always replaying from genesis.
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
    /// Performs full verification of a signed run against a deterministic simulation,
    /// optionally using signed state snapshots to accelerate replay.
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
    /// <param name="snapshots">
    /// Optional list of signed state snapshots that accelerate replay.  When provided,
    /// each simulation reset is replaced by loading the nearest valid snapshot ≤ the
    /// target tick.  All snapshots are pre-verified against <paramref name="runResult"/>
    /// using <see cref="VerifySnapshot"/> before use; any snapshot that fails verification
    /// is silently skipped.  When <see langword="null"/> or empty, the method falls back to
    /// full replay from genesis for every probe step.
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
        IDeterministicSimulation simulation,
        IReadOnlyList<StateSnapshot>? snapshots = null)
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
        {
            var verifiedSnapshots = BuildVerifiedSnapshotLookup(snapshots, runResult);
            return VerifyFinalHashByReplay(simulation, tickChain, runResult.FinalHash, verifiedSnapshots);
        }

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

        // Pre-verify snapshots once and build a sorted lookup keyed by TickIndex.
        var snapshotLookup = BuildVerifiedSnapshotLookup(snapshots, runResult);

        // Step 4: Binary-search checkpoint simulation.
        // Verify the first checkpoint by replaying from genesis (or nearest snapshot).
        InitSimulation(simulation, tickChain, 0, tickPositionLookup[checkpoints[0].TickIndex], snapshotLookup);
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
            var midEndIndex = tickPositionLookup[checkpoints[mid].TickIndex];

            InitSimulation(simulation, tickChain, 0, midEndIndex, snapshotLookup);

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
        var lowEndIndex = tickPositionLookup[checkpoints[low].TickIndex];
        InitSimulation(simulation, tickChain, 0, lowEndIndex, snapshotLookup);

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

    /// <summary>
    /// Verifies a <see cref="StateSnapshot"/> against the given session and run result.
    /// </summary>
    /// <param name="sessionId">The session's unique identifier.</param>
    /// <param name="snapshot">The snapshot to verify.</param>
    /// <param name="runResult">
    /// The signed run result whose <see cref="SignedRunResult.Checkpoints"/> list the
    /// snapshot tick must appear in.
    /// </param>
    /// <returns>
    /// <see langword="true"/> if all of the following hold:
    /// <list type="bullet">
    ///   <item>The authority signature on the snapshot is valid.</item>
    ///   <item><see cref="StateSnapshot.StateHash"/> is exactly 32 bytes.</item>
    ///   <item><see cref="StateSnapshot.TickIndex"/> matches a checkpoint in <paramref name="runResult"/>.</item>
    ///   <item><see cref="StateSnapshot.StateHash"/> equals the matching checkpoint hash.</item>
    /// </list>
    /// Returns <see langword="false"/> if any condition is not met.
    /// </returns>
    public bool VerifySnapshot(Guid sessionId, StateSnapshot snapshot, SignedRunResult runResult)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(runResult);

        // 1. Validate hash length (32 bytes).
        if (!snapshot.HasValidHashLength)
            return false;

        // 2. Verify the authority signature over the snapshot payload.
        if (snapshot.SerializedState is null || snapshot.Signature is null)
            return false;

        byte[] payloadHash;
        try
        {
            payloadHash = AuthorityCrypto.ComputeSnapshotPayloadHash(
                sessionId, snapshot.TickIndex, snapshot.StateHash, snapshot.SerializedState);
        }
        catch (ArgumentException)
        {
            return false;
        }

        if (!AuthorityCrypto.VerifyHashedPayload(_authority.PublicKeyBytes, payloadHash, snapshot.Signature))
            return false;

        // 3. Ensure the snapshot tick matches a checkpoint in the run result.
        var checkpoints = runResult.Checkpoints;
        if (checkpoints is null || checkpoints.Count == 0)
            return false;

        RunCheckpoint? matchingCheckpoint = null;
        foreach (var cp in checkpoints)
        {
            if (cp.TickIndex == snapshot.TickIndex)
            {
                matchingCheckpoint = cp;
                break;
            }
        }

        if (matchingCheckpoint is null)
            return false;

        // 4. Ensure the snapshot hash matches the checkpoint hash.
        return snapshot.StateHash.AsSpan().SequenceEqual(matchingCheckpoint.StateHash);
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
    /// Pre-verifies all snapshots and returns a sorted list of those that pass,
    /// keyed by <see cref="StateSnapshot.TickIndex"/>. The session ID is derived
    /// from <paramref name="runResult"/>. Invalid or null snapshots are silently skipped.
    /// Returns an empty list when <paramref name="snapshots"/> is null or empty.
    /// </summary>
    private List<StateSnapshot> BuildVerifiedSnapshotLookup(
        IReadOnlyList<StateSnapshot>? snapshots,
        SignedRunResult runResult)
    {
        if (snapshots is null || snapshots.Count == 0)
            return [];

        var verified = new List<StateSnapshot>(snapshots.Count);
        foreach (var snap in snapshots)
        {
            if (snap is not null && VerifySnapshot(runResult.SessionId, snap, runResult))
                verified.Add(snap);
        }

        // Sort ascending by TickIndex for binary-search-style lookups.
        verified.Sort(static (a, b) => a.TickIndex.CompareTo(b.TickIndex));
        return verified;
    }

    /// <summary>
    /// Finds the latest snapshot whose <see cref="StateSnapshot.TickIndex"/> is ≤
    /// <paramref name="targetTickChainIndex"/> (expressed as a position in the tick chain).
    /// Returns <see langword="null"/> when no suitable snapshot exists.
    /// </summary>
    private static StateSnapshot? FindBestSnapshot(
        List<StateSnapshot> verifiedSnapshots,
        IReadOnlyList<SignedTick> tickChain,
        int targetTickChainIndex)
    {
        if (verifiedSnapshots.Count == 0)
            return null;

        var targetTick = tickChain[targetTickChainIndex].Tick;
        StateSnapshot? best = null;

        // Linear scan; the list is sorted ascending, so the last eligible entry is the best.
        foreach (var snap in verifiedSnapshots)
        {
            if (snap.TickIndex <= targetTick)
                best = snap;
            else
                break;
        }

        return best;
    }

    /// <summary>
    /// Initialises the simulation to produce state at tick chain position
    /// <paramref name="endIndexInclusive"/>, using the best available snapshot when
    /// possible to avoid a full replay from genesis.
    /// </summary>
    private static void InitSimulation(
        IDeterministicSimulation simulation,
        IReadOnlyList<SignedTick> tickChain,
        int startIndexInclusive,
        int endIndexInclusive,
        List<StateSnapshot> verifiedSnapshots)
    {
        var snap = FindBestSnapshot(verifiedSnapshots, tickChain, endIndexInclusive);

        if (snap is not null)
        {
            // Find the position in tickChain immediately after the snapshot tick.
            // We need to apply all ticks after the snapshot up to endIndexInclusive.
            simulation.LoadState(snap.SerializedState);
            var snapTick = snap.TickIndex;
            var resumeFrom = -1;

            for (var i = 0; i <= endIndexInclusive; i++)
            {
                if (tickChain[i].Tick == snapTick)
                {
                    resumeFrom = i + 1;
                    break;
                }
            }

            if (resumeFrom < 0)
            {
                // Snapshot tick not found in range — fall back to full replay.
                simulation.Reset();
                ReplayUpTo(simulation, tickChain, endIndexInclusive);
                return;
            }

            for (var i = resumeFrom; i <= endIndexInclusive; i++)
                simulation.ApplyTick(tickChain[i]);
        }
        else
        {
            simulation.Reset();
            ReplayUpTo(simulation, tickChain, endIndexInclusive);
        }
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
        string finalHashHex,
        List<StateSnapshot> verifiedSnapshots)
    {
        var endIndex = tickChain.Count - 1;

        if (endIndex < 0)
        {
            simulation.Reset();
        }
        else
        {
            InitSimulation(simulation, tickChain, 0, endIndex, verifiedSnapshots);
        }

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
