using GUNRPG.Application.Sessions;
using GUNRPG.Core.Simulation;
using SessionsReplayRunner = GUNRPG.Application.Sessions.ReplayRunner;

namespace GUNRPG.Security;

/// <summary>
/// An authority node that can sign completed session runs and verify signed results.
/// Bridges the session replay system with the cryptographic authority infrastructure.
/// </summary>
public sealed class SessionAuthority : ISessionAuthority
{
    private readonly byte[] _privateKey;
    private readonly byte[] _publicKey;

    public SessionAuthority(byte[] privateKey, string id)
    {
        _privateKey = AuthorityCrypto.CloneAndValidatePrivateKey(privateKey);
        _publicKey = AuthorityCrypto.GetPublicKey(_privateKey);
        Id = string.IsNullOrWhiteSpace(id)
            ? throw new ArgumentException("Authority id must not be empty.", nameof(id))
            : id;
    }

    /// <summary>Unique identifier for this authority node.</summary>
    public string Id { get; }

    /// <summary>Public key bytes for this authority (Ed25519, 32 bytes).</summary>
    public byte[] PublicKey => (byte[])_publicKey.Clone();

    /// <summary>Returns a read-only <see cref="Authority"/> view of this node's public identity.</summary>
    public Authority ToAuthority() => new(_publicKey, Id);

    /// <summary>
    /// Signs a finalized session run and returns a <see cref="SignedRunResult"/>.
    /// </summary>
    /// <param name="sessionId">The session's unique identifier.</param>
    /// <param name="playerId">The player/operator unique identifier.</param>
    /// <param name="finalHashBytes">
    /// The SHA-256 hash produced by <see cref="CombatSessionHasher.ComputeStateHash"/> after replay.
    /// Must be exactly 32 bytes.
    /// </param>
    public SignedRunResult Sign(Guid sessionId, Guid playerId, byte[] finalHashBytes)
    {
        ArgumentNullException.ThrowIfNull(finalHashBytes);
        if (finalHashBytes.Length != System.Security.Cryptography.SHA256.HashSizeInBytes)
            throw new ArgumentException(
                $"finalHashBytes must be exactly {System.Security.Cryptography.SHA256.HashSizeInBytes} bytes (SHA-256).",
                nameof(finalHashBytes));

        var payloadHash = AuthorityCrypto.ComputeRunValidationPayloadHash(sessionId, playerId, finalHashBytes);
        var signature = AuthorityCrypto.SignHashedPayload(_privateKey, payloadHash);
        var finalHashHex = Convert.ToHexString(finalHashBytes);
        return new SignedRunResult(sessionId, playerId, finalHashHex, Id, signature);
    }

    /// <summary>
    /// Signs a finalized session run, binding both the final state hash and the full input-log
    /// replay hash. The Ed25519 signature covers: SessionId || PlayerId || FinalStateHash || ReplayHash.
    /// </summary>
    /// <param name="sessionId">The session's unique identifier.</param>
    /// <param name="playerId">The player/operator unique identifier.</param>
    /// <param name="finalHashBytes">SHA-256 hash of the replay-derived final state (32 bytes).</param>
    /// <param name="replayHashBytes">SHA-256 hash of the full input log (32 bytes).</param>
    public SignedRunResult Sign(
        Guid sessionId,
        Guid playerId,
        byte[] finalHashBytes,
        byte[] replayHashBytes)
    {
        ArgumentNullException.ThrowIfNull(finalHashBytes);
        ArgumentNullException.ThrowIfNull(replayHashBytes);
        if (finalHashBytes.Length != System.Security.Cryptography.SHA256.HashSizeInBytes)
            throw new ArgumentException(
                $"finalHashBytes must be exactly {System.Security.Cryptography.SHA256.HashSizeInBytes} bytes (SHA-256).",
                nameof(finalHashBytes));
        if (replayHashBytes.Length != System.Security.Cryptography.SHA256.HashSizeInBytes)
            throw new ArgumentException(
                $"replayHashBytes must be exactly {System.Security.Cryptography.SHA256.HashSizeInBytes} bytes (SHA-256).",
                nameof(replayHashBytes));

        var payloadHash = AuthorityCrypto.ComputeRunWithReplayPayloadHash(
            sessionId, playerId, finalHashBytes, replayHashBytes);
        var signature = AuthorityCrypto.SignHashedPayload(_privateKey, payloadHash);
        var finalHashHex = Convert.ToHexString(finalHashBytes);
        var replayHashHex = Convert.ToHexString(replayHashBytes);
        return new SignedRunResult(sessionId, playerId, finalHashHex, Id, signature, replayHashHex);
    }

    /// <summary>
    /// Signs a finalized session run, binding the final state hash, the full input-log
    /// replay hash, and the Merkle root of all tick leaf hashes.
    /// The Ed25519 signature covers: SessionId || PlayerId || FinalStateHash || ReplayHash || TickMerkleRoot.
    /// </summary>
    /// <param name="sessionId">The session's unique identifier.</param>
    /// <param name="playerId">The player/operator unique identifier.</param>
    /// <param name="finalHashBytes">SHA-256 hash of the replay-derived final state (32 bytes).</param>
    /// <param name="replayHashBytes">SHA-256 hash of the full input log (32 bytes).</param>
    /// <param name="tickMerkleRootBytes">
    /// Merkle root of all tick leaf hashes (32 bytes).
    /// Compute via <see cref="MerkleTree.ComputeRoot"/> from per-tick leaf hashes.
    /// </param>
    public SignedRunResult Sign(
        Guid sessionId,
        Guid playerId,
        byte[] finalHashBytes,
        byte[] replayHashBytes,
        byte[] tickMerkleRootBytes)
    {
        ArgumentNullException.ThrowIfNull(finalHashBytes);
        ArgumentNullException.ThrowIfNull(replayHashBytes);
        ArgumentNullException.ThrowIfNull(tickMerkleRootBytes);
        if (finalHashBytes.Length != System.Security.Cryptography.SHA256.HashSizeInBytes)
            throw new ArgumentException(
                $"finalHashBytes must be exactly {System.Security.Cryptography.SHA256.HashSizeInBytes} bytes (SHA-256).",
                nameof(finalHashBytes));
        if (replayHashBytes.Length != System.Security.Cryptography.SHA256.HashSizeInBytes)
            throw new ArgumentException(
                $"replayHashBytes must be exactly {System.Security.Cryptography.SHA256.HashSizeInBytes} bytes (SHA-256).",
                nameof(replayHashBytes));
        if (tickMerkleRootBytes.Length != System.Security.Cryptography.SHA256.HashSizeInBytes)
            throw new ArgumentException(
                $"tickMerkleRootBytes must be exactly {System.Security.Cryptography.SHA256.HashSizeInBytes} bytes (SHA-256).",
                nameof(tickMerkleRootBytes));

        var payloadHash = AuthorityCrypto.ComputeRunWithMerklePayloadHash(
            sessionId, playerId, finalHashBytes, replayHashBytes, tickMerkleRootBytes);
        var signature = AuthorityCrypto.SignHashedPayload(_privateKey, payloadHash);
        var finalHashHex = Convert.ToHexString(finalHashBytes);
        var replayHashHex = Convert.ToHexString(replayHashBytes);
        var merkleRootHex = Convert.ToHexString(tickMerkleRootBytes);
        return new SignedRunResult(sessionId, playerId, finalHashHex, Id, signature, replayHashHex, merkleRootHex);
    }

    /// <summary>
    /// Signs a finalized session run, binding the final state hash, the full input-log
    /// replay hash, the Merkle root of all tick leaf hashes, and the checkpoint list.
    /// The Ed25519 signature covers:
    /// SessionId || PlayerId || FinalStateHash || ReplayHash || TickMerkleRoot || Hash(Checkpoints).
    /// </summary>
    /// <param name="sessionId">The session's unique identifier.</param>
    /// <param name="playerId">The player/operator unique identifier.</param>
    /// <param name="finalHashBytes">SHA-256 hash of the replay-derived final state (32 bytes).</param>
    /// <param name="replayHashBytes">SHA-256 hash of the full input log (32 bytes).</param>
    /// <param name="tickMerkleRootBytes">
    /// Merkle root of all tick leaf hashes (32 bytes).
    /// Compute via <see cref="MerkleTree.ComputeRoot"/> from per-tick leaf hashes.
    /// </param>
    /// <param name="checkpoints">
    /// Ordered list of state-hash checkpoints recorded during simulation.
    /// Must not be <see langword="null"/>; may be empty.
    /// </param>
    public SignedRunResult Sign(
        Guid sessionId,
        Guid playerId,
        byte[] finalHashBytes,
        byte[] replayHashBytes,
        byte[] tickMerkleRootBytes,
        IReadOnlyList<RunCheckpoint> checkpoints)
    {
        ArgumentNullException.ThrowIfNull(finalHashBytes);
        ArgumentNullException.ThrowIfNull(replayHashBytes);
        ArgumentNullException.ThrowIfNull(tickMerkleRootBytes);
        ArgumentNullException.ThrowIfNull(checkpoints);
        if (finalHashBytes.Length != System.Security.Cryptography.SHA256.HashSizeInBytes)
            throw new ArgumentException(
                $"finalHashBytes must be exactly {System.Security.Cryptography.SHA256.HashSizeInBytes} bytes (SHA-256).",
                nameof(finalHashBytes));
        if (replayHashBytes.Length != System.Security.Cryptography.SHA256.HashSizeInBytes)
            throw new ArgumentException(
                $"replayHashBytes must be exactly {System.Security.Cryptography.SHA256.HashSizeInBytes} bytes (SHA-256).",
                nameof(replayHashBytes));
        if (tickMerkleRootBytes.Length != System.Security.Cryptography.SHA256.HashSizeInBytes)
            throw new ArgumentException(
                $"tickMerkleRootBytes must be exactly {System.Security.Cryptography.SHA256.HashSizeInBytes} bytes (SHA-256).",
                nameof(tickMerkleRootBytes));

        var checkpointsHash = AuthorityCrypto.ComputeCheckpointsHash(checkpoints);
        var payloadHash = AuthorityCrypto.ComputeRunWithCheckpointsPayloadHash(
            sessionId, playerId, finalHashBytes, replayHashBytes, tickMerkleRootBytes, checkpointsHash);
        var signature = AuthorityCrypto.SignHashedPayload(_privateKey, payloadHash);
        var finalHashHex = Convert.ToHexString(finalHashBytes);
        var replayHashHex = Convert.ToHexString(replayHashBytes);
        var merkleRootHex = Convert.ToHexString(tickMerkleRootBytes);
        return new SignedRunResult(
            sessionId, playerId, finalHashHex, Id, signature, replayHashHex, merkleRootHex, checkpoints);
    }

    /// <summary>
    /// Creates and signs a <see cref="StateSnapshot"/> at a checkpoint tick boundary.
    /// </summary>
    /// <param name="sessionId">The session's unique identifier.</param>
    /// <param name="tickIndex">The checkpoint tick index at which the snapshot was captured.</param>
    /// <param name="stateHash">
    /// SHA-256 hash of the simulation state at <paramref name="tickIndex"/> (32 bytes).
    /// Must match the corresponding <see cref="RunCheckpoint.StateHash"/> in the run result.
    /// </param>
    /// <param name="serializedState">
    /// Deterministic serialization of the simulation state, produced by
    /// <see cref="IDeterministicSimulation.SerializeState"/>.
    /// </param>
    /// <returns>
    /// A <see cref="StateSnapshot"/> whose <see cref="StateSnapshot.Signature"/> covers:
    /// <c>SessionId || TickIndex || StateHash || SerializedState</c>.
    /// </returns>
    public StateSnapshot CreateSnapshot(
        Guid sessionId,
        long tickIndex,
        byte[] stateHash,
        byte[] serializedState)
    {
        ArgumentNullException.ThrowIfNull(stateHash);
        ArgumentNullException.ThrowIfNull(serializedState);
        if (stateHash.Length != System.Security.Cryptography.SHA256.HashSizeInBytes)
            throw new ArgumentException(
                $"stateHash must be exactly {System.Security.Cryptography.SHA256.HashSizeInBytes} bytes (SHA-256).",
                nameof(stateHash));

        var payloadHash = AuthorityCrypto.ComputeSnapshotPayloadHash(
            sessionId, tickIndex, stateHash, serializedState);
        var signature = AuthorityCrypto.SignHashedPayload(_privateKey, payloadHash);

        return new StateSnapshot(
            tickIndex,
            (byte[])stateHash.Clone(),
            (byte[])serializedState.Clone(),
            (byte[])signature.Clone());
    }

    /// <summary>
    /// Creates a <see cref="RunReceipt"/> from a verified signed run result.
    /// </summary>
    /// <param name="run">
    /// The signed run result for which to create a receipt.
    /// Must have a non-null <see cref="SignedRunResult.TickMerkleRoot"/>.
    /// </param>
    /// <returns>
    /// A <see cref="RunReceipt"/> whose <see cref="RunReceipt.Signature"/> covers the
    /// canonical receipt payload computed by
    /// <see cref="AuthorityCrypto.ComputeReceiptPayloadHash(Guid,long,byte[],byte[])"/> for
    /// <c>SessionId</c>, <c>FinalTick</c>, <c>FinalStateHash</c>, and <c>TickMerkleRoot</c>
    /// (including the length-prefixed encodings of the hash values).
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="run"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="run"/> does not have a <see cref="SignedRunResult.TickMerkleRoot"/>.
    /// </exception>
    public RunReceipt CreateRunReceipt(SignedRunResult run)
    {
        ArgumentNullException.ThrowIfNull(run);

        if (run.TickMerkleRoot is null)
            throw new ArgumentException(
                "Run must have a TickMerkleRoot to create a receipt.", nameof(run));

        var finalStateHash = Convert.FromHexString(run.FinalHash);
        var tickMerkleRoot = Convert.FromHexString(run.TickMerkleRoot);
        var finalTick = run.Checkpoints is not null && run.Checkpoints.Count > 0
            ? run.Checkpoints[^1].TickIndex
            : 0L;

        var payloadHash = AuthorityCrypto.ComputeReceiptPayloadHash(
            run.SessionId, finalTick, finalStateHash, tickMerkleRoot);
        var signature = AuthorityCrypto.SignHashedPayload(_privateKey, payloadHash);

        return new RunReceipt(
            run.SessionId,
            finalTick,
            (byte[])finalStateHash.Clone(),
            (byte[])tickMerkleRoot.Clone(),
            signature);
    }

    /// <summary>
    /// Signs a simulation tick and returns the raw Ed25519 signature (64 bytes).
    /// The signature covers: Tick (big-endian int64) || PrevStateHash || StateHash || InputHash.
    /// </summary>
    /// <inheritdoc cref="ISessionAuthority.SignTick"/>
    public byte[] SignTick(long tick, byte[] prevStateHash, byte[] stateHash, byte[] inputHash)
    {
        var payloadHash = AuthorityCrypto.ComputeTickPayloadHash(tick, prevStateHash, stateHash, inputHash);
        return AuthorityCrypto.SignHashedPayload(_privateKey, payloadHash);
    }

    /// <summary>
    /// Creates and signs a <see cref="MerkleCheckpoint"/> at the given simulation tick.
    /// Only authority nodes (those that hold a private key) can produce checkpoints.
    /// </summary>
    /// <param name="tick">The simulation tick this checkpoint represents.</param>
    /// <param name="merkleRoot">
    /// The simulation state hash at <paramref name="tick"/> (SHA-256, exactly 32 bytes).
    /// </param>
    /// <returns>
    /// A new <see cref="MerkleCheckpoint"/> whose <see cref="MerkleCheckpoint.Signature"/> is an
    /// Ed25519 signature over
    /// <c>SHA-256("checkpoint" || tick (big-endian uint64) || merkleRoot)</c>.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="merkleRoot"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="merkleRoot"/> is not exactly 32 bytes.</exception>
    public MerkleCheckpoint CreateMerkleCheckpoint(ulong tick, byte[] merkleRoot)
    {
        ArgumentNullException.ThrowIfNull(merkleRoot);
        if (merkleRoot.Length != System.Security.Cryptography.SHA256.HashSizeInBytes)
            throw new ArgumentException(
                $"merkleRoot must be exactly {System.Security.Cryptography.SHA256.HashSizeInBytes} bytes (SHA-256).",
                nameof(merkleRoot));

        var payloadHash = AuthorityCrypto.ComputeMerkleCheckpointPayloadHash(tick, merkleRoot);
        var signature = AuthorityCrypto.SignHashedPayload(_privateKey, payloadHash);
        return new MerkleCheckpoint(tick, (byte[])merkleRoot.Clone(), (byte[])_publicKey.Clone(), signature);
    }

    /// <summary>
    /// Verifies a <see cref="MerkleCheckpoint"/>:
    /// <list type="number">
    ///   <item>Validates field lengths (<see cref="MerkleCheckpoint.HasValidStructure"/>).</item>
    ///   <item>Checks that <see cref="MerkleCheckpoint.AuthorityPublicKey"/> is trusted by <paramref name="registry"/>.</item>
    ///   <item>Verifies the Ed25519 signature.</item>
    /// </list>
    /// </summary>
    /// <param name="checkpoint">The checkpoint to verify.</param>
    /// <param name="registry">The registry of trusted authority public keys.</param>
    /// <returns>
    /// <see langword="true"/> if all three checks pass; <see langword="false"/> otherwise.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="checkpoint"/> or <paramref name="registry"/> is <see langword="null"/>.
    /// </exception>
    public static bool VerifyMerkleCheckpoint(MerkleCheckpoint checkpoint, AuthorityRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        ArgumentNullException.ThrowIfNull(registry);

        if (!checkpoint.HasValidStructure)
            return false;

        if (!registry.IsTrustedAuthority(checkpoint.AuthorityPublicKey))
            return false;

        byte[] payloadHash;
        try
        {
            payloadHash = AuthorityCrypto.ComputeMerkleCheckpointPayloadHash(checkpoint.Tick, checkpoint.MerkleRoot);
        }
        catch (ArgumentException)
        {
            return false;
        }

        try
        {
            return AuthorityCrypto.VerifyHashedPayload(checkpoint.AuthorityPublicKey, payloadHash, checkpoint.Signature);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// <summary>
    /// Verifies the Ed25519 signature on a <see cref="SignedTick"/> using this authority's public key.
    /// </summary>
    /// <inheritdoc cref="ISessionAuthority.VerifyTick"/>
    public bool VerifyTick(SignedTick signedTick)
    {
        ArgumentNullException.ThrowIfNull(signedTick);
        var payloadHash = AuthorityCrypto.ComputeTickPayloadHash(
            signedTick.Tick, signedTick.PrevStateHash, signedTick.StateHash, signedTick.InputHash);
        return AuthorityCrypto.VerifyHashedPayload(_publicKey, payloadHash, signedTick.Signature);
    }

    /// <summary>
    /// Verifies that a <see cref="SignedRunResult"/> was produced by the given <paramref name="authority"/>.
    /// Returns <see langword="false"/> if the authority ID does not match, the signature is invalid,
    /// or <see cref="SignedRunResult.FinalHash"/> cannot be decoded as a valid 32-byte hex value.
    /// When <see cref="SignedRunResult.TickMerkleRoot"/> is present the signature is verified against
    /// the full Merkle payload (SessionId || PlayerId || FinalStateHash || ReplayHash || TickMerkleRoot).
    /// When only <see cref="SignedRunResult.ReplayHash"/> is present the signature is verified against the
    /// combined payload (SessionId || PlayerId || FinalStateHash || ReplayHash).
    /// </summary>
    public static bool VerifySignedRun(SignedRunResult result, Authority authority)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(authority);

        if (!string.Equals(result.AuthorityId, authority.Id, StringComparison.Ordinal))
            return false;

        byte[] finalHashBytes;
        try
        {
            finalHashBytes = Convert.FromHexString(result.FinalHash);
        }
        catch (FormatException)
        {
            return false;
        }

        if (finalHashBytes.Length != System.Security.Cryptography.SHA256.HashSizeInBytes)
            return false;

        byte[] payloadHash;

        if (result.ReplayHash is not null && result.TickMerkleRoot is not null)
        {
            // Result was signed with the Merkle payload (FinalStateHash + ReplayHash + TickMerkleRoot)
            // or the Checkpoints payload (FinalStateHash + ReplayHash + TickMerkleRoot + Hash(Checkpoints)).
            byte[] replayHashBytes;
            byte[] merkleRootBytes;
            try
            {
                replayHashBytes = Convert.FromHexString(result.ReplayHash);
                merkleRootBytes = Convert.FromHexString(result.TickMerkleRoot);
            }
            catch (FormatException)
            {
                return false;
            }

            if (replayHashBytes.Length != System.Security.Cryptography.SHA256.HashSizeInBytes)
                return false;
            if (merkleRootBytes.Length != System.Security.Cryptography.SHA256.HashSizeInBytes)
                return false;

            if (result.Checkpoints is not null)
            {
                // Checkpoints payload: includes Hash(Checkpoints) in the signature.
                var checkpointsHash = AuthorityCrypto.ComputeCheckpointsHash(result.Checkpoints);
                payloadHash = AuthorityCrypto.ComputeRunWithCheckpointsPayloadHash(
                    result.SessionId, result.PlayerId, finalHashBytes, replayHashBytes, merkleRootBytes, checkpointsHash);
            }
            else
            {
                payloadHash = AuthorityCrypto.ComputeRunWithMerklePayloadHash(
                    result.SessionId, result.PlayerId, finalHashBytes, replayHashBytes, merkleRootBytes);
            }
        }
        else if (result.ReplayHash is not null)
        {
            // Result was signed with the combined payload (FinalStateHash + ReplayHash).
            byte[] replayHashBytes;
            try
            {
                replayHashBytes = Convert.FromHexString(result.ReplayHash);
            }
            catch (FormatException)
            {
                return false;
            }

            if (replayHashBytes.Length != System.Security.Cryptography.SHA256.HashSizeInBytes)
                return false;

            payloadHash = AuthorityCrypto.ComputeRunWithReplayPayloadHash(
                result.SessionId, result.PlayerId, finalHashBytes, replayHashBytes);
        }
        else
        {
            payloadHash = AuthorityCrypto.ComputeRunValidationPayloadHash(
                result.SessionId,
                result.PlayerId,
                finalHashBytes);
        }

        return AuthorityCrypto.VerifyHashedPayload(authority.PublicKeyBytes, payloadHash, result.SignatureBytes);
    }

    /// <summary>
    /// Verifies a <see cref="SignedRunResult"/> by replaying the session from
    /// <paramref name="replayInitialSnapshotJson"/> and <paramref name="replayTurns"/>,
    /// then checking both the deterministic hash and the authority signature.
    /// </summary>
    /// <remarks>
    /// This constitutes the full authority validation pipeline:
    /// <list type="number">
    ///   <item>Replay the session deterministically.</item>
    ///   <item>Compute the hash of the replayed final state.</item>
    ///   <item>Reject if the replay hash does not match <see cref="SignedRunResult.FinalHash"/>.</item>
    ///   <item>Verify the Ed25519 signature using the authority's public key.</item>
    /// </list>
    /// </remarks>
    public static bool VerifySignedRunWithReplay(
        SignedRunResult result,
        Authority authority,
        string replayInitialSnapshotJson,
        IReadOnlyList<IntentSnapshot> replayTurns)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(authority);
        ArgumentException.ThrowIfNullOrWhiteSpace(replayInitialSnapshotJson);
        ArgumentNullException.ThrowIfNull(replayTurns);

        // Step 1: Replay the session deterministically.
        var replayState = SessionsReplayRunner.Run(replayInitialSnapshotJson, replayTurns);

        // Step 2: Compute hash from the replayed final snapshot.
        var replayHashBytes = CombatSessionHasher.ComputeStateHash(replayState.Snapshot);
        var replayHashHex = Convert.ToHexString(replayHashBytes);

        // Step 3: Reject if the replay hash does not match the signed hash.
        if (!string.Equals(replayHashHex, result.FinalHash, StringComparison.OrdinalIgnoreCase))
            return false;

        // Step 4: Verify the authority signature.
        return VerifySignedRun(result, authority);
    }

    /// <summary>
    /// Generates a new random Ed25519 private key suitable for use with <see cref="SessionAuthority"/>.
    /// </summary>
    public static byte[] GeneratePrivateKey() => AuthorityCrypto.GeneratePrivateKey();
}
