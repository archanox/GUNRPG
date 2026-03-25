using System.Buffers.Binary;
using System.Security.Cryptography;
using GUNRPG.Core.Simulation;

namespace GUNRPG.Security;

/// <summary>
/// Coordinates per-tick authority signing for the deterministic simulation loop.
/// On the <see cref="NodeRole.Authority"/> node, signs a checkpoint every
/// <see cref="SignInterval"/> ticks. All nodes can verify received
/// <see cref="SignedTick"/> instances and detect state desyncs.
/// </summary>
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

    private readonly SessionAuthority _authority;
    private readonly IStateHasher _stateHasher;

    /// <summary>
    /// Initialises a new <see cref="TickAuthorityService"/>.
    /// </summary>
    /// <param name="authority">The <see cref="SessionAuthority"/> used for signing and verification.</param>
    /// <param name="stateHasher">Optional state hasher. Defaults to <see cref="StateHasher"/>.</param>
    public TickAuthorityService(SessionAuthority authority, IStateHasher? stateHasher = null)
    {
        _authority = authority ?? throw new ArgumentNullException(nameof(authority));
        _stateHasher = stateHasher ?? new StateHasher();
    }

    /// <summary>
    /// Processes a tick on the <see cref="NodeRole.Authority"/> node:
    /// computes the state hash and, every <see cref="SignInterval"/> ticks,
    /// signs a <see cref="SignedTick"/> checkpoint.
    /// </summary>
    /// <param name="tick">The simulation tick number.</param>
    /// <param name="state">The simulation state produced after this tick.</param>
    /// <param name="action">The player action that drove this tick.</param>
    /// <returns>
    /// The per-tick <see cref="TickState"/> and, when <paramref name="tick"/> is a checkpoint,
    /// the signed <see cref="SignedTick"/>. Otherwise <see cref="SignedTick"/> is <see langword="null"/>.
    /// </returns>
    public (TickState TickState, SignedTick? SignedTick) ProcessTick(
        long tick,
        SimulationState state,
        PlayerAction action)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(action);

        var stateHash = _stateHasher.HashTick(tick, state);
        var inputHash = HashAction(action);
        return BuildTickResult(tick, stateHash, inputHash);
    }

    /// <summary>
    /// Processes a tick on the <see cref="NodeRole.Authority"/> node using a pre-computed input hash.
    /// Useful when the input hash is computed externally or represents a batch of inputs.
    /// </summary>
    public (TickState TickState, SignedTick? SignedTick) ProcessTick(
        long tick,
        SimulationState state,
        byte[] inputHash)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(inputHash);

        var stateHash = _stateHasher.HashTick(tick, state);
        return BuildTickResult(tick, stateHash, inputHash);
    }

    /// <summary>
    /// Verifies a <see cref="SignedTick"/> on a <see cref="NodeRole.Validator"/> or
    /// <see cref="NodeRole.Client"/> node, checking both the cryptographic signature
    /// and the local state hash for desync.
    /// </summary>
    /// <param name="signedTick">The signed tick received from the authority.</param>
    /// <param name="localStateHash">The state hash computed locally after simulating this tick.</param>
    /// <exception cref="InvalidSignatureException">
    /// Thrown when the Ed25519 signature on <paramref name="signedTick"/> is invalid.
    /// </exception>
    /// <exception cref="DesyncException">
    /// Thrown when <paramref name="localStateHash"/> does not match
    /// <see cref="SignedTick.StateHash"/>.
    /// </exception>
    public void VerifySignedTickOrThrow(SignedTick signedTick, byte[] localStateHash)
    {
        ArgumentNullException.ThrowIfNull(signedTick);
        ArgumentNullException.ThrowIfNull(localStateHash);

        if (!_authority.VerifyTick(signedTick))
            throw new InvalidSignatureException(signedTick.Tick);

        if (!localStateHash.AsSpan().SequenceEqual(signedTick.StateHash))
            throw new DesyncException(signedTick.Tick, signedTick.StateHash, localStateHash);
    }

    /// <summary>
    /// Finalises a run by producing a <see cref="SignedRunResult"/> that binds both the
    /// final state hash (from the last verified tick) and the full input-log replay hash.
    /// The Ed25519 signature covers: SessionId &#x2016; PlayerId &#x2016; FinalStateHash &#x2016; ReplayHash.
    /// </summary>
    /// <param name="sessionId">Unique identifier of the session.</param>
    /// <param name="playerId">Unique identifier of the player/operator.</param>
    /// <param name="finalStateHash">
    /// SHA-256 hash of the simulation state at the end of the run.
    /// Must match <see cref="TickState.StateHash"/> of the last verified tick.
    /// </param>
    /// <param name="replayHash">
    /// SHA-256 hash of the full input log, as computed by
    /// <see cref="ReplayRunner.Replay(InputLog)"/> → <see cref="ReplayResult.FinalHash"/>.
    /// </param>
    /// <returns>
    /// A <see cref="SignedRunResult"/> whose signature covers both hashes,
    /// guaranteeing the run cannot be persisted without full input-log verification.
    /// </returns>
    public SignedRunResult FinalizeRun(
        Guid sessionId,
        Guid playerId,
        byte[] finalStateHash,
        byte[] replayHash)
    {
        ArgumentNullException.ThrowIfNull(finalStateHash);
        ArgumentNullException.ThrowIfNull(replayHash);

        return _authority.Sign(sessionId, playerId, finalStateHash, replayHash);
    }

    /// <summary>
    /// Computes the SHA-256 hash of the canonical serialized form of a player action,
    /// suitable for inclusion in <see cref="TickInput.InputHash"/> and <see cref="SignedTick.InputHash"/>.
    /// </summary>
    public static byte[] HashAction(PlayerAction action)
    {
        ArgumentNullException.ThrowIfNull(action);

        // Encode: type discriminator (int32 big-endian) followed by action-specific payload.
        // Mirrors the action encoding in StateHasher.WriteAction for cross-machine consistency.
        const int intSize = 4;
        return action switch
        {
            MoveAction move => SHA256.HashData(EncodeInt32Pair(1, (int)move.Direction)),
            AttackAction attack => SHA256.HashData(EncodeGuidWithTag(2, attack.TargetId)),
            UseItemAction useItem => SHA256.HashData(EncodeGuidWithTag(3, useItem.ItemId)),
            ExfilAction => SHA256.HashData(EncodeInt32Pair(4, 0)),
            _ => throw new InvalidOperationException(
                $"Cannot hash unknown action type '{action.GetType().Name}'."),
        };

        static byte[] EncodeInt32Pair(int discriminator, int value)
        {
            var buf = new byte[intSize + intSize];
            BinaryPrimitives.WriteInt32BigEndian(buf, discriminator);
            BinaryPrimitives.WriteInt32BigEndian(buf.AsSpan(intSize), value);
            return buf;
        }

        static byte[] EncodeGuidWithTag(int discriminator, Guid id)
        {
            var buf = new byte[intSize + 16];
            BinaryPrimitives.WriteInt32BigEndian(buf, discriminator);
            if (!id.TryWriteBytes(buf.AsSpan(intSize), bigEndian: true, out _))
                throw new InvalidOperationException("Failed to encode GUID for input hashing.");
            return buf;
        }
    }

    private (TickState TickState, SignedTick? SignedTick) BuildTickResult(
        long tick,
        byte[] stateHash,
        byte[] inputHash)
    {
        var tickState = new TickState(tick, stateHash);

        SignedTick? signedTick = null;
        if (tick % SignInterval == 0)
        {
            var signature = _authority.SignTick(tick, stateHash, inputHash);
            signedTick = new SignedTick(tick, stateHash, inputHash, signature);
        }

        return (tickState, signedTick);
    }
}
