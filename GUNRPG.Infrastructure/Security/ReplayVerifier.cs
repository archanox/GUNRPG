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

        // Build a O(1) lookup from tick value to position in tickChain.
        // Built here (before the checkpoint guard) so it can be shared with the
        // no-checkpoint fallback path and every InitSimulation call.
        var tickPositionLookup = BuildTickPositionLookup(tickChain);

        // Pre-verify snapshots once and build a sorted lookup keyed by TickIndex.
        var snapshotLookup = BuildVerifiedSnapshotLookup(snapshots, runResult);

        // No checkpoints: fall back to a direct full replay to confirm the final state hash.
        if (checkpoints is null || checkpoints.Count == 0)
            return VerifyFinalHashByReplay(simulation, tickChain, runResult.FinalHash, snapshotLookup, tickPositionLookup);

        // Defensively verify that every checkpoint tick index is present in the lookup.
        // VerifyCheckpoints (step 3) enforces this invariant, but we guard here to prevent
        // KeyNotFoundException on any subsequent lookup access.
        foreach (var cp in checkpoints)
        {
            if (!tickPositionLookup.ContainsKey(cp.TickIndex))
                return false;
        }

        // Step 4: Binary-search checkpoint simulation.
        // Verify the first checkpoint by replaying from genesis (or nearest snapshot).
        InitSimulation(simulation, tickChain, tickPositionLookup[checkpoints[0].TickIndex], snapshotLookup, tickPositionLookup);
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

            InitSimulation(simulation, tickChain, midEndIndex, snapshotLookup, tickPositionLookup);

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
        InitSimulation(simulation, tickChain, lowEndIndex, snapshotLookup, tickPositionLookup);

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

        bool signatureValid;
        try
        {
            signatureValid = AuthorityCrypto.VerifyHashedPayload(_authority.PublicKeyBytes, payloadHash, snapshot.Signature);
        }
        catch (ArgumentException)
        {
            return false;
        }

        if (!signatureValid)
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
    /// After a successful <see cref="IDeterministicSimulation.LoadState"/> the loaded hash
    /// is validated against the snapshot's <see cref="StateSnapshot.StateHash"/>; if they
    /// differ (indicating an inconsistent or buggy <c>LoadState</c> implementation) the
    /// method falls back to <see cref="IDeterministicSimulation.Reset"/> + full replay.
    /// </summary>
    private static void InitSimulation(
        IDeterministicSimulation simulation,
        IReadOnlyList<SignedTick> tickChain,
        int endIndexInclusive,
        List<StateSnapshot> verifiedSnapshots,
        Dictionary<long, int> tickPositionLookup)
    {
        var snap = FindBestSnapshot(verifiedSnapshots, tickChain, endIndexInclusive);

        if (snap is not null)
        {
            simulation.LoadState(snap.SerializedState);

            // Validate that the loaded state hash matches the snapshot's StateHash.
            // If it doesn't (inconsistent or buggy LoadState), fall back to full replay.
            var loadedHash = simulation.GetStateHash();
            if (!IsValidStateHash(loadedHash) || !loadedHash.AsSpan().SequenceEqual(snap.StateHash))
            {
                simulation.Reset();
                ReplayUpTo(simulation, tickChain, endIndexInclusive);
                return;
            }

            // Use O(1) lookup to find the snapshot tick position in the chain.
            if (!tickPositionLookup.TryGetValue(snap.TickIndex, out var snapPos))
            {
                // Snapshot tick not found in chain — fall back to full replay.
                simulation.Reset();
                ReplayUpTo(simulation, tickChain, endIndexInclusive);
                return;
            }

            var resumeFrom = snapPos + 1;
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
        List<StateSnapshot> verifiedSnapshots,
        Dictionary<long, int> tickPositionLookup)
    {
        if (tickChain.Count == 0)
        {
            simulation.Reset();
        }
        else
        {
            InitSimulation(simulation, tickChain, tickChain.Count - 1, verifiedSnapshots, tickPositionLookup);
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

    // ──────────────────────────────────────────────────────────────────────────
    // Merkle Proof-of-Divergence
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Performs full verification of a signed run against a deterministic simulation,
    /// optionally using signed state snapshots to accelerate replay, and—when the run
    /// is invalid due to simulation divergence—produces a cryptographic
    /// <see cref="DivergenceProof"/> identifying the exact divergent tick.
    /// </summary>
    /// <param name="tickChain">The complete, ordered chain of signed tick checkpoints.</param>
    /// <param name="runResult">The signed run result to verify.</param>
    /// <param name="simulation">A deterministic simulation used to re-execute the tick chain.</param>
    /// <param name="divergenceProof">
    /// When this method returns <see langword="false"/> due to simulation divergence,
    /// receives a <see cref="DivergenceProof"/> that cryptographically identifies the
    /// divergent tick and can be verified by any third party using only the
    /// <see cref="SignedRunResult.TickMerkleRoot"/> from <paramref name="runResult"/>.
    /// Set to <see langword="null"/> when verification passes or when it fails for a
    /// non-simulation reason (invalid signature, Merkle root mismatch, etc.).
    /// </param>
    /// <param name="snapshots">
    /// Optional list of signed state snapshots that accelerate binary-search replay.
    /// </param>
    /// <returns>
    /// <see langword="true"/> if every verification step passes;
    /// <see langword="false"/> if any check fails.
    /// </returns>
    public bool TryVerifyRun(
        IReadOnlyList<SignedTick> tickChain,
        SignedRunResult runResult,
        IDeterministicSimulation simulation,
        out DivergenceProof? divergenceProof,
        IReadOnlyList<StateSnapshot>? snapshots = null)
    {
        ArgumentNullException.ThrowIfNull(tickChain);
        ArgumentNullException.ThrowIfNull(runResult);
        ArgumentNullException.ThrowIfNull(simulation);

        divergenceProof = null;

        // Guard: tick chain must be strictly ordered and contain no null entries.
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

        // Step 3: Verify checkpoint structure.
        if (!TickAuthorityService.VerifyCheckpoints(runResult, tickChain))
            return false;

        var checkpoints = runResult.Checkpoints;
        var tickPositionLookup = BuildTickPositionLookup(tickChain);
        var snapshotLookup = BuildVerifiedSnapshotLookup(snapshots, runResult);

        // No checkpoints: fall back to per-tick linear replay with divergence detection.
        if (checkpoints is null || checkpoints.Count == 0)
        {
            return TryVerifyFinalHashWithDivergence(
                simulation, tickChain, runResult.FinalHash,
                snapshotLookup, tickPositionLookup, out divergenceProof);
        }

        // Defensively verify every checkpoint tick index is in the lookup.
        foreach (var cp in checkpoints)
        {
            if (!tickPositionLookup.ContainsKey(cp.TickIndex))
                return false;
        }

        // Verify the first checkpoint.
        var firstEndIndex = tickPositionLookup[checkpoints[0].TickIndex];
        InitSimulation(simulation, tickChain, firstEndIndex, snapshotLookup, tickPositionLookup);
        var firstHash = simulation.GetStateHash();
        if (!IsValidStateHash(firstHash) || !firstHash.AsSpan().SequenceEqual(checkpoints[0].StateHash))
        {
            // Divergence is somewhere in [0, firstEndIndex]: scan tick by tick.
            var leafHashesForFirst = ComputeLeafHashes(tickChain);
            divergenceProof = FindDivergenceInRange(simulation, tickChain, leafHashesForFirst, 0, firstEndIndex);
            return false;
        }

        // Single-checkpoint run: the one checkpoint must be the final tick.
        if (checkpoints.Count == 1)
        {
            if (checkpoints[0].TickIndex != tickChain[^1].Tick)
                return false;

            return TryParseHexHash(runResult.FinalHash, out var singleFinalHash)
                   && firstHash.AsSpan().SequenceEqual(singleFinalHash);
        }

        // Binary search to narrow the divergence window.
        var low = 0;
        var high = checkpoints.Count - 1;

        while (high - low > 1)
        {
            var mid = low + (high - low) / 2;
            var midEndIndex = tickPositionLookup[checkpoints[mid].TickIndex];

            InitSimulation(simulation, tickChain, midEndIndex, snapshotLookup, tickPositionLookup);
            var midHash = simulation.GetStateHash();

            if (IsValidStateHash(midHash) && midHash.AsSpan().SequenceEqual(checkpoints[mid].StateHash))
                low = mid;
            else
                high = mid;
        }

        // Linear replay within [low, high] with per-tick divergence detection.
        var lowEndIndex = tickPositionLookup[checkpoints[low].TickIndex];
        var highEndIndex = tickPositionLookup[checkpoints[high].TickIndex];
        InitSimulation(simulation, tickChain, lowEndIndex, snapshotLookup, tickPositionLookup);

        // Pre-compute leaf hashes once so BuildDivergenceProof can reuse them
        // without recomputing the entire chain inside CreateDivergenceProof.
        var leafHashes = ComputeLeafHashes(tickChain);

        for (var i = lowEndIndex + 1; i <= highEndIndex; i++)
        {
            simulation.ApplyTick(tickChain[i]);
            var hash = simulation.GetStateHash();
            if (!IsValidStateHash(hash) || !hash.AsSpan().SequenceEqual(tickChain[i].StateHash))
            {
                divergenceProof = BuildDivergenceProof(leafHashes, i, tickChain[i], hash);
                return false;
            }
        }

        // Validate the final signed state hash.
        var highHash = simulation.GetStateHash();
        if (!IsValidStateHash(highHash) || !highHash.AsSpan().SequenceEqual(checkpoints[high].StateHash))
            return false;

        if (checkpoints[^1].TickIndex != tickChain[^1].Tick)
            return false;

        return TryParseHexHash(runResult.FinalHash, out var signedFinalHash)
               && highHash.AsSpan().SequenceEqual(signedFinalHash);
    }

    /// <summary>
    /// Creates a <see cref="DivergenceProof"/> for the tick at <paramref name="divergentTick"/>
    /// within the given <paramref name="tickChain"/>.
    /// </summary>
    /// <param name="tickChain">The complete ordered tick chain that was signed.</param>
    /// <param name="divergentTick">The tick number where divergence was detected.</param>
    /// <param name="expectedLeafHash">
    /// The 32-byte tick leaf hash committed to the Merkle tree for the divergent tick
    /// (computed via <see cref="TickAuthorityService.ComputeTickLeafHash"/>).
    /// </param>
    /// <param name="actualHash">
    /// The 32-byte hash produced by the local simulation at the divergent tick.
    /// </param>
    /// <returns>A <see cref="DivergenceProof"/> that can be verified against the run's Merkle root.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="tickChain"/>, <paramref name="expectedLeafHash"/>, or
    /// <paramref name="actualHash"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="divergentTick"/> is not found in <paramref name="tickChain"/>.
    /// </exception>
    public static DivergenceProof CreateDivergenceProof(
        IReadOnlyList<SignedTick> tickChain,
        long divergentTick,
        byte[] expectedLeafHash,
        byte[] actualHash)
    {
        ArgumentNullException.ThrowIfNull(tickChain);
        ArgumentNullException.ThrowIfNull(expectedLeafHash);
        ArgumentNullException.ThrowIfNull(actualHash);

        // Find the zero-based leaf index of the divergent tick.
        var leafIndex = -1;
        for (var i = 0; i < tickChain.Count; i++)
        {
            if (tickChain[i].Tick == divergentTick)
            {
                leafIndex = i;
                break;
            }
        }

        if (leafIndex < 0)
            throw new ArgumentException(
                $"Tick {divergentTick} was not found in the tick chain.", nameof(divergentTick));

        // Build the full list of Merkle leaf hashes (computed once and passed to proof generation).
        var leafHashes = ComputeLeafHashes(tickChain);

        return CreateDivergenceProofFromLeaves(leafHashes, leafIndex, divergentTick, expectedLeafHash, actualHash);
    }

    /// <summary>
    /// Verifies a <see cref="DivergenceProof"/> against the Merkle root from a signed run.
    /// </summary>
    /// <remarks>
    /// Verification steps:
    /// <list type="number">
    ///   <item>Reject structurally invalid proofs (null/wrong-length hash fields).</item>
    ///   <item>
    ///     Apply the Merkle inclusion proof: reconstruct the Merkle root from
    ///     <see cref="DivergenceProof.ExpectedTickHash"/> and the sibling hashes in
    ///     <see cref="DivergenceProof.MerkleProof"/> and confirm it equals
    ///     <paramref name="merkleRoot"/>.
    ///   </item>
    ///   <item>
    ///     Confirm divergence: <see cref="DivergenceProof.ExpectedTickHash"/> must differ from
    ///     <see cref="DivergenceProof.ActualTickHash"/>.
    ///   </item>
    /// </list>
    /// </remarks>
    /// <param name="proof">The divergence proof to verify.</param>
    /// <param name="merkleRoot">
    /// The 32-byte Merkle root from <see cref="SignedRunResult.TickMerkleRoot"/>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the proof is structurally valid, the Merkle inclusion
    /// check passes, and the expected and actual hashes differ (confirming divergence).
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="proof"/> or <paramref name="merkleRoot"/> is
    /// <see langword="null"/>.
    /// </exception>
    public static bool VerifyDivergenceProof(DivergenceProof proof, byte[] merkleRoot)
    {
        ArgumentNullException.ThrowIfNull(proof);
        ArgumentNullException.ThrowIfNull(merkleRoot);

        // Reject structurally invalid proof structures.
        if (!proof.IsStructurallyValid)
            return false;

        if (merkleRoot.Length != SHA256.HashSizeInBytes)
            return false;

        // Depth limit: reject proofs deeper than MaxMerkleProofDepth (DoS protection).
        // MerkleTree.VerifyProof also enforces this, but the early-exit here avoids
        // allocating a MerkleProof wrapper for clearly malformed inputs.
        if (proof.MerkleProof.Count > MerkleTree.MaxMerkleProofDepth)
            return false;

        // LeafIndex range check: for a tree of depth d (d sibling hashes), valid leaf
        // indices are [0, 2^d - 1]. For depth >= 31 every non-negative int is in range,
        // so only check when the shift would not overflow an int.
        var proofDepth = proof.MerkleProof.Count;
        if (proofDepth <= 30 && proof.LeafIndex >= (1 << proofDepth))
            return false;

        // Build a MerkleProof from the DivergenceProof's fields and verify Merkle inclusion.
        var inclusionProof = new MerkleProof(
            (byte[])proof.ExpectedTickHash.Clone(),
            proof.MerkleProof,
            proof.LeafIndex);

        bool inclusionValid;
        try
        {
            inclusionValid = MerkleTree.VerifyProof(inclusionProof, (byte[])merkleRoot.Clone());
        }
        catch (ArgumentException)
        {
            return false;
        }

        if (!inclusionValid)
            return false;

        // Confirm that expected and actual hashes differ, proving divergence.
        return !proof.ExpectedTickHash.AsSpan().SequenceEqual(proof.ActualTickHash);
    }

    /// <summary>
    /// Full per-tick linear replay for runs without checkpoints.
    /// Detects the first divergent tick and produces a proof; falls back to a simple
    /// final-hash comparison when no divergence is detected.
    /// </summary>
    private static bool TryVerifyFinalHashWithDivergence(
        IDeterministicSimulation simulation,
        IReadOnlyList<SignedTick> tickChain,
        string finalHashHex,
        List<StateSnapshot> verifiedSnapshots,
        Dictionary<long, int> tickPositionLookup,
        out DivergenceProof? divergenceProof)
    {
        divergenceProof = null;

        if (tickChain.Count == 0)
        {
            simulation.Reset();
            var emptyHash = simulation.GetStateHash();
            return TryParseHexHash(finalHashHex, out var fh)
                   && IsValidStateHash(emptyHash)
                   && emptyHash.AsSpan().SequenceEqual(fh);
        }

        // Pre-compute leaf hashes once so proof generation doesn't recompute them.
        var leafHashes = ComputeLeafHashes(tickChain);

        // Linear replay from genesis, checking each signed tick.
        simulation.Reset();
        for (var i = 0; i < tickChain.Count; i++)
        {
            simulation.ApplyTick(tickChain[i]);
            var hash = simulation.GetStateHash();
            if (!IsValidStateHash(hash) || !hash.AsSpan().SequenceEqual(tickChain[i].StateHash))
            {
                divergenceProof = BuildDivergenceProof(leafHashes, i, tickChain[i], hash);
                return false;
            }
        }

        var simulatedHash = simulation.GetStateHash();
        return TryParseHexHash(finalHashHex, out var finalHash)
               && IsValidStateHash(simulatedHash)
               && simulatedHash.AsSpan().SequenceEqual(finalHash);
    }

    /// <summary>
    /// Replays the tick chain from genesis (tick by tick) from <paramref name="startIndex"/>
    /// to <paramref name="endIndex"/> inclusive and returns a <see cref="DivergenceProof"/>
    /// for the first tick whose simulated state hash does not match the signed state hash.
    /// Returns <see langword="null"/> if no divergence is found in the range.
    /// </summary>
    private static DivergenceProof? FindDivergenceInRange(
        IDeterministicSimulation simulation,
        IReadOnlyList<SignedTick> tickChain,
        IReadOnlyList<byte[]> leafHashes,
        int startIndex,
        int endIndex)
    {
        simulation.Reset();
        for (var i = startIndex; i <= endIndex; i++)
        {
            simulation.ApplyTick(tickChain[i]);
            var hash = simulation.GetStateHash();
            if (!IsValidStateHash(hash) || !hash.AsSpan().SequenceEqual(tickChain[i].StateHash))
                return BuildDivergenceProof(leafHashes, i, tickChain[i], hash);
        }

        return null;
    }

    /// <summary>
    /// Constructs a <see cref="DivergenceProof"/> for a tick whose signed state hash
    /// does not match the simulation output, using pre-computed leaf hashes to avoid
    /// recomputing the full tick chain inside <see cref="CreateDivergenceProof"/>.
    /// </summary>
    private static DivergenceProof BuildDivergenceProof(
        IReadOnlyList<byte[]> leafHashes,
        int leafIndex,
        SignedTick divergentTick,
        byte[]? actualStateHash)
    {
        var expectedLeafHash = TickAuthorityService.ComputeTickLeafHash(
            divergentTick.Tick, divergentTick.PrevStateHash,
            divergentTick.StateHash, divergentTick.InputHash);

        // Use a zero-filled sentinel when the simulation returned a null or wrong-length hash.
        // The sentinel satisfies DivergenceProof's 32-byte requirement and still differs from
        // the expected leaf hash (which is a SHA-256 hash of real tick data), so the proof
        // correctly reports divergence. The original invalid value cannot be stored directly
        // because DivergenceProof enforces a fixed 32-byte size.
        var actualHash = IsValidStateHash(actualStateHash)
            ? actualStateHash
            : new byte[SHA256.HashSizeInBytes];

        return CreateDivergenceProofFromLeaves(leafHashes, leafIndex, divergentTick.Tick, expectedLeafHash, actualHash);
    }

    /// <summary>
    /// Computes the Merkle leaf hash for every tick in <paramref name="tickChain"/> and
    /// returns them as an ordered list.  This is factored out so callers can compute the
    /// hashes once and reuse the array across multiple proof-generation calls.
    /// </summary>
    private static List<byte[]> ComputeLeafHashes(IReadOnlyList<SignedTick> tickChain)
    {
        var leafHashes = new List<byte[]>(tickChain.Count);
        foreach (var t in tickChain)
            leafHashes.Add(TickAuthorityService.ComputeTickLeafHash(
                t.Tick, t.PrevStateHash, t.StateHash, t.InputHash));
        return leafHashes;
    }

    /// <summary>
    /// Core proof-construction helper.  Generates the Merkle inclusion proof from
    /// pre-computed <paramref name="leafHashes"/> and assembles the
    /// <see cref="DivergenceProof"/> record.
    /// </summary>
    private static DivergenceProof CreateDivergenceProofFromLeaves(
        IReadOnlyList<byte[]> leafHashes,
        int leafIndex,
        long tickIndex,
        byte[] expectedLeafHash,
        byte[] actualHash)
    {
        var merkleProof = MerkleTree.GenerateProof(leafHashes, leafIndex);

        return new DivergenceProof(
            tickIndex,
            leafIndex,
            (byte[])expectedLeafHash.Clone(),
            (byte[])actualHash.Clone(),
            merkleProof.SiblingHashes);
    }
}
