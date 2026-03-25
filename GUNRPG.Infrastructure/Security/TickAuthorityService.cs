using System.Security.Cryptography;
using GUNRPG.Core.Simulation;

namespace GUNRPG.Security;

/// <summary>
/// Coordinates per-tick authority signing for the deterministic simulation loop.
/// On the <see cref="NodeRole.Authority"/> node, signs a checkpoint every
/// <see cref="SignInterval"/> ticks. All nodes can verify received
/// <see cref="SignedTick"/> instances and detect state desyncs.
/// </summary>
/// <remarks>
/// Hash chaining: each <see cref="SignedTick"/> includes the state hash of the
/// previous signed checkpoint (<see cref="SignedTick.PrevStateHash"/>), which is covered
/// by the Ed25519 signature. This prevents valid ticks from being replayed or spliced from
/// a different simulation timeline.
/// Checkpoint continuity: <see cref="VerifyTickChain"/> enforces that checkpoints are strictly
/// increasing in tick number, and that each checkpoint's <see cref="SignedTick.PrevStateHash"/>
/// equals the previous checkpoint's <see cref="SignedTick.StateHash"/>.
/// Sequential continuity: <see cref="VerifySignedTickOrThrow"/> enforces strict +1 tick
/// ordering when a <c>previousSignedTick</c> is provided, suitable for sequential per-tick use.
/// </remarks>
public sealed class TickAuthorityService
{
    /// <summary>
    /// Number of ticks between successive signed checkpoints in production.
    /// Every <c>SignInterval</c>-th tick (including tick 0) is signed by the authority.
    /// Intermediate ticks are validated via deterministic replay only.
    /// </summary>
    /// <remarks>
    /// The value 10 represents a trade-off between signature overhead and desync detection
    /// granularity: signing every tick would be maximally secure but expensive; a larger
    /// interval increases the window during which undetected divergence can accumulate.
    /// At typical game tick rates (e.g. 60 Hz) this means one cryptographic checkpoint
    /// approximately every 160 ms, with intermediate ticks validated through replay.
    /// </remarks>
    public const int SignInterval = 10;

    /// <summary>
    /// Returns a copy of the canonical "previous state hash" used for the very first signed
    /// checkpoint (tick 0). This is 32 zero bytes, ensuring the genesis tick is unambiguously
    /// the start of the chain.
    /// A new copy is returned on each call to prevent callers from mutating the shared sentinel.
    /// </summary>
    public static byte[] GenesisStateHash => new byte[SHA256.HashSizeInBytes];

    private readonly ITickVerifier _verifier;
    private readonly SessionAuthority? _signer;
    private readonly IStateHasher _stateHasher;

    /// <summary>
    /// Initialises a new <see cref="TickAuthorityService"/> for an authority node that can
    /// both sign checkpoints and verify them.
    /// </summary>
    /// <param name="authority">
    /// The <see cref="SessionAuthority"/> used for both signing and verification.
    /// Must not be <see langword="null"/>.
    /// </param>
    /// <param name="stateHasher">Optional state hasher. Defaults to <see cref="StateHasher"/>.</param>
    public TickAuthorityService(SessionAuthority authority, IStateHasher? stateHasher = null)
    {
        ArgumentNullException.ThrowIfNull(authority);
        _verifier = authority;
        _signer = authority;
        _stateHasher = stateHasher ?? new StateHasher();
    }

    /// <summary>
    /// Initialises a new <see cref="TickAuthorityService"/> for a validator or client node
    /// that can only verify checkpoints (no private key required).
    /// </summary>
    /// <param name="verifier">
    /// A verifier that holds only the authority's public key.
    /// Must not be <see langword="null"/>.
    /// </param>
    /// <param name="stateHasher">Optional state hasher. Defaults to <see cref="StateHasher"/>.</param>
    public TickAuthorityService(ITickVerifier verifier, IStateHasher? stateHasher = null)
    {
        _verifier = verifier ?? throw new ArgumentNullException(nameof(verifier));
        _signer = null;
        _stateHasher = stateHasher ?? new StateHasher();
    }

    /// <summary>
    /// Processes a tick on the <see cref="NodeRole.Authority"/> node:
    /// computes the state hash and, every <see cref="SignInterval"/> ticks,
    /// signs a chained <see cref="SignedTick"/> checkpoint.
    /// </summary>
    /// <param name="tick">The simulation tick number.</param>
    /// <param name="state">The simulation state produced after this tick.</param>
    /// <param name="inputs">
    /// The deterministically-ordered batch of player inputs for this tick.
    /// Use <see cref="TickInputs"/> to guarantee canonical ordering across nodes.
    /// </param>
    /// <param name="prevSignedStateHash">
    /// The <see cref="SignedTick.StateHash"/> of the most recent prior signed checkpoint,
    /// or <see cref="GenesisStateHash"/> if this is the first checkpoint.
    /// Included in the signature payload to form an unforgeable hash chain.
    /// </param>
    /// <returns>
    /// The per-tick <see cref="TickState"/> and, when <paramref name="tick"/> is a checkpoint,
    /// the signed <see cref="SignedTick"/>. Otherwise <see cref="SignedTick"/> is <see langword="null"/>.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when this service was created without a signer (verifier-only constructor).
    /// </exception>
    public (TickState TickState, SignedTick? SignedTick) ProcessTick(
        long tick,
        SimulationState state,
        TickInputs inputs,
        byte[] prevSignedStateHash)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(prevSignedStateHash);

        var stateHash = _stateHasher.HashTick(tick, state);
        var inputHash = inputs.ComputeHash();
        return BuildTickResult(tick, prevSignedStateHash, stateHash, inputHash);
    }

    /// <summary>
    /// Processes a single-player tick on the <see cref="NodeRole.Authority"/> node.
    /// Creates a single-entry <see cref="TickInputs"/> from the given player and action.
    /// </summary>
    /// <param name="tick">The simulation tick number.</param>
    /// <param name="state">The simulation state produced after this tick.</param>
    /// <param name="playerId">The player's unique identifier.</param>
    /// <param name="action">The player's action for this tick.</param>
    /// <param name="prevSignedStateHash">
    /// The <see cref="SignedTick.StateHash"/> of the most recent prior signed checkpoint,
    /// or <see cref="GenesisStateHash"/> for the first checkpoint.
    /// </param>
    /// <exception cref="InvalidOperationException">
    /// Thrown when this service was created without a signer (verifier-only constructor).
    /// </exception>
    public (TickState TickState, SignedTick? SignedTick) ProcessTick(
        long tick,
        SimulationState state,
        Guid playerId,
        PlayerAction action,
        byte[] prevSignedStateHash)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(prevSignedStateHash);

        var inputs = new TickInputs(tick, [new PlayerInput(playerId, action)]);
        var stateHash = _stateHasher.HashTick(tick, state);
        var inputHash = inputs.ComputeHash();
        return BuildTickResult(tick, prevSignedStateHash, stateHash, inputHash);
    }

    /// <summary>
    /// Processes a tick on the <see cref="NodeRole.Authority"/> node using a pre-computed input hash.
    /// Useful when the input hash is computed externally or represents a batch of inputs.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when this service was created without a signer (verifier-only constructor).
    /// </exception>
    public (TickState TickState, SignedTick? SignedTick) ProcessTick(
        long tick,
        SimulationState state,
        byte[] inputHash,
        byte[] prevSignedStateHash)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(inputHash);
        ArgumentNullException.ThrowIfNull(prevSignedStateHash);

        var stateHash = _stateHasher.HashTick(tick, state);
        return BuildTickResult(tick, prevSignedStateHash, stateHash, inputHash);
    }

    /// <summary>
    /// Verifies a <see cref="SignedTick"/> on a <see cref="NodeRole.Validator"/> or
    /// <see cref="NodeRole.Client"/> node, enforcing:
    /// <list type="number">
    ///   <item>The Ed25519 signature is valid.</item>
    ///   <item>The local state hash matches <see cref="SignedTick.StateHash"/> (no desync).</item>
    ///   <item>
    ///     If <paramref name="previousSignedTick"/> is provided:
    ///     <c>signedTick.Tick == previousSignedTick.Tick + 1</c> (sequential tick continuity).
    ///   </item>
    ///   <item>
    ///     If <paramref name="previousSignedTick"/> is provided:
    ///     <c>signedTick.PrevStateHash == previousSignedTick.StateHash</c> (hash-chain integrity).
    ///   </item>
    /// </list>
    /// For checkpoint-to-checkpoint chain validation (where ticks advance by
    /// <see cref="SignInterval"/>, not 1), use <see cref="VerifyTickChain"/> which enforces
    /// strictly-increasing tick ordering rather than +1.
    /// </summary>
    /// <param name="signedTick">The signed tick received from the authority.</param>
    /// <param name="localStateHash">The state hash computed locally after simulating this tick.</param>
    /// <param name="previousSignedTick">
    /// The immediately preceding tick, or <see langword="null"/> for the genesis tick.
    /// When provided, sequential continuity (<c>+1</c>) and hash-chain integrity are enforced.
    /// </param>
    /// <exception cref="InvalidSignatureException">
    /// Thrown when the Ed25519 signature on <paramref name="signedTick"/> is invalid.
    /// </exception>
    /// <exception cref="DesyncException">
    /// Thrown when <paramref name="localStateHash"/> does not match
    /// <see cref="SignedTick.StateHash"/>, or when the hash chain is broken.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Thrown when sequential tick continuity is violated
    /// (<c>signedTick.Tick != previousSignedTick.Tick + 1</c>).
    /// </exception>
    public void VerifySignedTickOrThrow(
        SignedTick signedTick,
        byte[] localStateHash,
        SignedTick? previousSignedTick = null)
    {
        ArgumentNullException.ThrowIfNull(signedTick);
        ArgumentNullException.ThrowIfNull(localStateHash);

        // 1. Signature validation (covers Tick || PrevStateHash || StateHash || InputHash)
        if (!_verifier.VerifyTick(signedTick))
            throw new InvalidSignatureException(signedTick.Tick);

        // 2. Local state desync check
        if (!localStateHash.AsSpan().SequenceEqual(signedTick.StateHash))
            throw new DesyncException(signedTick.Tick, signedTick.StateHash, localStateHash);

        if (previousSignedTick is not null)
        {
            // 3. Sequential tick continuity (use VerifyTickChain for checkpoint-to-checkpoint validation)
            if (signedTick.Tick != previousSignedTick.Tick + 1)
                throw new ArgumentException(
                    $"Tick continuity violation: expected tick {previousSignedTick.Tick + 1}, " +
                    $"but received tick {signedTick.Tick}.",
                    nameof(signedTick));

            // 4. Hash-chain integrity: PrevStateHash must equal the previous tick's StateHash
            if (!signedTick.PrevStateHash.AsSpan().SequenceEqual(previousSignedTick.StateHash))
                throw new DesyncException(
                    signedTick.Tick,
                    previousSignedTick.StateHash,
                    signedTick.PrevStateHash);
        }
    }

    /// <summary>
    /// Validates a complete chain of signed checkpoints and throws on the first violation.
    /// Ensures:
    /// <list type="bullet">
    ///   <item>The genesis checkpoint has the all-zero <see cref="GenesisStateHash"/> sentinel.</item>
    ///   <item>
    ///     Every signature is valid.
    ///   </item>
    ///   <item>
    ///     Checkpoint tick numbers are strictly increasing
    ///     (<c>ticks[i].Tick &gt; ticks[i-1].Tick</c>), matching the checkpoint spacing
    ///     of <see cref="SignInterval"/> ticks between signatures.
    ///   </item>
    ///   <item>
    ///     Every hash-chain link is intact
    ///     (<c>ticks[i].PrevStateHash == ticks[i-1].StateHash</c>).
    ///   </item>
    ///   <item>
    ///     When <paramref name="localStateHashes"/> is provided, the local state hashes match
    ///     (no desync occurred during simulation).
    ///   </item>
    /// </list>
    /// This must pass before a run may be finalised and persisted.
    /// For sequential per-tick verification (not checkpoint-to-checkpoint), use
    /// <see cref="VerifySignedTickOrThrow"/> which enforces strict <c>+1</c> ordering.
    /// </summary>
    /// <param name="ticks">The ordered chain of signed checkpoints from genesis to the final tick.</param>
    /// <param name="localStateHashes">
    /// Optional array of local state hashes parallel to <paramref name="ticks"/>.
    /// When provided, desync detection is applied at every checkpoint.
    /// </param>
    /// <exception cref="InvalidSignatureException">Thrown when any signature is invalid.</exception>
    /// <exception cref="DesyncException">
    /// Thrown when any state hash mismatch, hash-chain break, or genesis sentinel violation is detected.
    /// </exception>
    /// <exception cref="ArgumentException">Thrown on checkpoint-continuity violation (non-increasing ticks).</exception>
    public void VerifyTickChain(
        IReadOnlyList<SignedTick> ticks,
        IReadOnlyList<byte[]>? localStateHashes = null)
    {
        ArgumentNullException.ThrowIfNull(ticks);

        if (localStateHashes is not null && localStateHashes.Count != ticks.Count)
            throw new ArgumentException(
                $"localStateHashes must have the same length as ticks ({ticks.Count}), " +
                $"but has {localStateHashes.Count} entries.",
                nameof(localStateHashes));

        for (var i = 0; i < ticks.Count; i++)
        {
            var tick = ticks[i] ?? throw new ArgumentException(
                $"Tick at index {i} must not be null.", nameof(ticks));

            var localHash = localStateHashes?[i] ?? tick.StateHash;

            // Genesis checkpoint: PrevStateHash must be all-zero bytes.
            if (i == 0)
            {
                var genesisHash = GenesisStateHash;
                if (!tick.PrevStateHash.AsSpan().SequenceEqual(genesisHash))
                    throw new DesyncException(tick.Tick, genesisHash, tick.PrevStateHash);
            }
            else
            {
                var previous = ticks[i - 1];

                // Checkpoint continuity: ticks must be strictly increasing.
                // Checkpoints are spaced SignInterval ticks apart; we allow any positive delta
                // rather than requiring exactly +1, so a normal sequence (0, 10, 20, ...) is valid.
                if (tick.Tick <= previous.Tick)
                    throw new ArgumentException(
                        $"Tick at index {i} (tick={tick.Tick}) must be greater than the previous " +
                        $"checkpoint tick {previous.Tick}.",
                        nameof(ticks));

                // Hash-chain integrity: PrevStateHash must equal the previous checkpoint's StateHash.
                if (!tick.PrevStateHash.AsSpan().SequenceEqual(previous.StateHash))
                    throw new DesyncException(tick.Tick, previous.StateHash, tick.PrevStateHash);
            }

            // Verify signature and local state hash without enforcing sequential +1 continuity.
            VerifySignedTickOrThrow(tick, localHash, previousSignedTick: null);
        }
    }

    /// <summary>
    /// Finalises a run after successful chain verification, producing a
    /// <see cref="SignedRunResult"/> that binds both the final state hash and the
    /// full input-log replay hash.
    /// </summary>
    /// <remarks>
    /// This overload first calls <see cref="VerifyTickChain(IReadOnlyList{SignedTick}, IReadOnlyList{byte[]})"/>
    /// to fully verify the chain (signatures, continuity, hash links, genesis sentinel), then
    /// checks that <paramref name="finalStateHash"/> matches the last verified tick before signing.
    /// Enforces the rule: no valid <see cref="SignedTick"/> chain → no persistence.
    /// </remarks>
    /// <param name="sessionId">Unique identifier of the session.</param>
    /// <param name="playerId">Unique identifier of the player/operator.</param>
    /// <param name="finalStateHash">
    /// SHA-256 hash of the simulation state at the end of the run.
    /// Must equal <see cref="SignedTick.StateHash"/> of the last tick in
    /// <paramref name="verifiedTickChain"/>.
    /// </param>
    /// <param name="replayHash">
    /// SHA-256 hash of the full input log (<see cref="ReplayResult.FinalHash"/>).
    /// </param>
    /// <param name="verifiedTickChain">
    /// The chain of signed checkpoints to verify and finalise. The chain is fully verified
    /// internally — the caller does not need to call <see cref="VerifyTickChain"/> separately.
    /// </param>
    /// <returns>
    /// A <see cref="SignedRunResult"/> whose signature covers both hashes.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when this service was created without a signer (verifier-only constructor),
    /// or when <paramref name="finalStateHash"/> does not match the last tick's
    /// <see cref="SignedTick.StateHash"/>.
    /// </exception>
    /// <exception cref="InvalidSignatureException">Thrown when any tick signature is invalid.</exception>
    /// <exception cref="DesyncException">Thrown when any hash-chain violation is detected.</exception>
    public SignedRunResult FinalizeRun(
        Guid sessionId,
        Guid playerId,
        byte[] finalStateHash,
        byte[] replayHash,
        IReadOnlyList<SignedTick> verifiedTickChain)
    {
        ArgumentNullException.ThrowIfNull(finalStateHash);
        ArgumentNullException.ThrowIfNull(replayHash);
        ArgumentNullException.ThrowIfNull(verifiedTickChain);

        EnsureSigner();

        // Fully verify the chain before allowing persistence.
        VerifyTickChain(verifiedTickChain);

        if (verifiedTickChain.Count > 0)
        {
            var lastTick = verifiedTickChain[^1]!;
            if (!finalStateHash.AsSpan().SequenceEqual(lastTick.StateHash))
                throw new InvalidOperationException(
                    "finalStateHash does not match the last verified tick's StateHash. " +
                    "The run cannot be finalised without a complete verified tick chain.");
        }

        return _signer!.Sign(sessionId, playerId, finalStateHash, replayHash);
    }

    /// <summary>
    /// Finalises a run by producing a <see cref="SignedRunResult"/> that binds both the
    /// final state hash and the full input-log replay hash.
    /// Use the overload with <c>verifiedTickChain</c> to enforce full chain verification
    /// at the persistence boundary.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when this service was created without a signer (verifier-only constructor).
    /// </exception>
    public SignedRunResult FinalizeRun(
        Guid sessionId,
        Guid playerId,
        byte[] finalStateHash,
        byte[] replayHash)
    {
        ArgumentNullException.ThrowIfNull(finalStateHash);
        ArgumentNullException.ThrowIfNull(replayHash);

        EnsureSigner();
        return _signer!.Sign(sessionId, playerId, finalStateHash, replayHash);
    }

    /// <summary>
    /// Computes the SHA-256 hash of a <see cref="TickInputs"/> batch.
    /// This is the canonical method for producing the <c>InputHash</c> field of a
    /// <see cref="SignedTick"/>.
    /// </summary>
    public static byte[] HashInputs(TickInputs inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        return inputs.ComputeHash();
    }

    /// <summary>
    /// Computes the SHA-256 hash of a single player action, producing a <c>TickInputs</c>
    /// batch for a single-player tick.
    /// For multi-player scenarios use <see cref="HashInputs(TickInputs)"/> with a
    /// properly-constructed <see cref="TickInputs"/>.
    /// </summary>
    public static byte[] HashSingleAction(long tick, Guid playerId, PlayerAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        return new TickInputs(tick, [new PlayerInput(playerId, action)]).ComputeHash();
    }

    private void EnsureSigner()
    {
        if (_signer is null)
            throw new InvalidOperationException(
                "This TickAuthorityService was created with a verifier-only ITickVerifier. " +
                "Signing operations require a SessionAuthority with a private key.");
    }

    private (TickState TickState, SignedTick? SignedTick) BuildTickResult(
        long tick,
        byte[] prevSignedStateHash,
        byte[] stateHash,
        byte[] inputHash)
    {
        var tickState = new TickState(tick, stateHash);

        SignedTick? signedTick = null;
        if (tick % SignInterval == 0)
        {
            EnsureSigner();
            var signature = _signer!.SignTick(tick, prevSignedStateHash, stateHash, inputHash);
            signedTick = new SignedTick(tick, prevSignedStateHash, stateHash, inputHash, signature);
        }

        return (tickState, signedTick);
    }
}
