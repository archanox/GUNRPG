using System.Buffers.Binary;
using System.Security.Cryptography;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Math.EC.Rfc8032;

namespace GUNRPG.Security;

public sealed class AuthorityRoot
{
    private readonly byte[] _publicKey;
    private readonly RevokedServerIds _revokedServerIds;

    public AuthorityRoot(byte[] publicKey, RevokedServerIds? revokedServerIds = null)
    {
        _publicKey = AuthorityCrypto.CloneAndValidatePublicKey(publicKey);
        _revokedServerIds = revokedServerIds ?? RevokedServerIds.Empty;
    }

    public byte[] PublicKey => (byte[])_publicKey.Clone();

    public bool VerifyServerCertificate(ServerCertificate cert, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(cert);

        if (cert.ValidUntil <= cert.IssuedAt || cert.IssuedAt > now || cert.ValidUntil < now)
        {
            return false;
        }

        if (_revokedServerIds.IsRevoked(cert.ServerId))
        {
            return false;
        }

        return AuthorityCrypto.VerifyHashedPayload(
            _publicKey,
            cert.ComputePayloadHash(),
            cert.Signature);
    }
}

internal static class AuthorityCrypto
{
    internal const int KeySize = 32;
    internal const int SignatureSize = 64;
    private const int GuidSize = 16;
    private const int Int64Size = 8;
    private const int Int32Size = 4;

    internal static byte[] GeneratePrivateKey()
    {
        return RandomNumberGenerator.GetBytes(KeySize);
    }

    internal static byte[] GetPublicKey(byte[] privateKey)
    {
        var normalizedPrivateKey = CloneAndValidatePrivateKey(privateKey);
        return new Ed25519PrivateKeyParameters(normalizedPrivateKey, 0).GeneratePublicKey().GetEncoded();
    }

    internal static byte[] SignPayload(byte[] privateKey, byte[] payload)
    {
        var normalizedPrivateKey = CloneAndValidatePrivateKey(privateKey);
        ArgumentNullException.ThrowIfNull(payload);

        var signature = new byte[SignatureSize];
        new Ed25519PrivateKeyParameters(normalizedPrivateKey, 0).Sign(
            Ed25519.Algorithm.Ed25519,
            null, // No RFC 8032 context: signs the raw payload bytes directly; callers pre-hash when needed.
            payload,
            signature);
        return signature;
    }

    internal static bool VerifyPayload(byte[] publicKey, byte[] payload, byte[] signature)
    {
        var normalizedPublicKey = CloneAndValidatePublicKey(publicKey);
        ArgumentNullException.ThrowIfNull(payload);
        var normalizedSignature = CloneAndValidateSignature(signature);

        return new Ed25519PublicKeyParameters(normalizedPublicKey, 0).Verify(
            Ed25519.Algorithm.Ed25519,
            null, // No RFC 8032 context: verification must match the plain Ed25519 signing mode above.
            payload,
            normalizedSignature);
    }

    internal static byte[] SignHashedPayload(byte[] privateKey, byte[] payloadHash)
    {
        var normalizedHash = CloneAndValidateSha256Hash(payloadHash);
        return SignPayload(privateKey, normalizedHash);
    }

    internal static bool VerifyHashedPayload(byte[] publicKey, byte[] payloadHash, byte[] signature)
    {
        var normalizedHash = CloneAndValidateSha256Hash(payloadHash);
        return VerifyPayload(publicKey, normalizedHash, signature);
    }

    internal static byte[] ComputeCertificatePayloadHash(
        Guid serverId,
        byte[] publicKey,
        DateTimeOffset issuedAt,
        DateTimeOffset validUntil)
    {
        var normalizedPublicKey = CloneAndValidatePublicKey(publicKey);
        var buffer = new byte[GuidSize + Int32Size + normalizedPublicKey.Length + Int64Size + Int64Size];
        var offset = 0;

        WriteGuid(serverId, buffer, ref offset);
        WriteLengthPrefixed(normalizedPublicKey, buffer, ref offset);
        WriteInt64(issuedAt.ToUnixTimeMilliseconds(), buffer, ref offset);
        WriteInt64(validUntil.ToUnixTimeMilliseconds(), buffer, ref offset);

        return SHA256.HashData(buffer);
    }

    internal static byte[] ComputeRunValidationPayloadHash(Guid runId, Guid playerId, byte[] finalStateHash)
    {
        var normalizedFinalStateHash = CloneAndValidateSha256Hash(finalStateHash);
        var buffer = new byte[GuidSize + GuidSize + Int32Size + normalizedFinalStateHash.Length];
        var offset = 0;

        WriteGuid(runId, buffer, ref offset);
        WriteGuid(playerId, buffer, ref offset);
        WriteLengthPrefixed(normalizedFinalStateHash, buffer, ref offset);

        return SHA256.HashData(buffer);
    }

    /// <summary>
    /// Computes the signing payload hash for a run result that includes both the final state hash
    /// and a replay hash. The signature covers: SessionId || PlayerId || FinalStateHash || ReplayHash.
    /// </summary>
    internal static byte[] ComputeRunWithReplayPayloadHash(
        Guid sessionId,
        Guid playerId,
        byte[] finalStateHash,
        byte[] replayHash)
    {
        var normalizedFinalStateHash = CloneAndValidateSha256Hash(finalStateHash);
        var normalizedReplayHash = CloneAndValidateSha256Hash(replayHash);
        var buffer = new byte[
            GuidSize + GuidSize
            + Int32Size + normalizedFinalStateHash.Length
            + Int32Size + normalizedReplayHash.Length];
        var offset = 0;

        WriteGuid(sessionId, buffer, ref offset);
        WriteGuid(playerId, buffer, ref offset);
        WriteLengthPrefixed(normalizedFinalStateHash, buffer, ref offset);
        WriteLengthPrefixed(normalizedReplayHash, buffer, ref offset);

        return SHA256.HashData(buffer);
    }

    /// <summary>
    /// Computes the signing payload hash for a run result that includes the final state hash,
    /// a replay hash, and the Merkle root of all tick leaf hashes.
    /// The signature covers: SessionId || PlayerId || FinalStateHash || ReplayHash || TickMerkleRoot.
    /// </summary>
    internal static byte[] ComputeRunWithMerklePayloadHash(
        Guid sessionId,
        Guid playerId,
        byte[] finalStateHash,
        byte[] replayHash,
        byte[] tickMerkleRoot)
    {
        var normalizedFinalStateHash = CloneAndValidateSha256Hash(finalStateHash);
        var normalizedReplayHash = CloneAndValidateSha256Hash(replayHash);
        var normalizedMerkleRoot = CloneAndValidateSha256Hash(tickMerkleRoot);
        var buffer = new byte[
            GuidSize + GuidSize
            + Int32Size + normalizedFinalStateHash.Length
            + Int32Size + normalizedReplayHash.Length
            + Int32Size + normalizedMerkleRoot.Length];
        var offset = 0;

        WriteGuid(sessionId, buffer, ref offset);
        WriteGuid(playerId, buffer, ref offset);
        WriteLengthPrefixed(normalizedFinalStateHash, buffer, ref offset);
        WriteLengthPrefixed(normalizedReplayHash, buffer, ref offset);
        WriteLengthPrefixed(normalizedMerkleRoot, buffer, ref offset);

        return SHA256.HashData(buffer);
    }

    /// <summary>
    /// Computes the canonical hash of a checkpoint list.
    /// Encoding: <c>count (big-endian int32) || for each: TickIndex (big-endian int64) || StateHash (32 bytes)</c>.
    /// An empty list produces <c>SHA-256(00 00 00 00)</c> (hash of a zero count).
    /// </summary>
    internal static byte[] ComputeCheckpointsHash(IReadOnlyList<RunCheckpoint> checkpoints)
    {
        ArgumentNullException.ThrowIfNull(checkpoints);

        // Each entry: 8 bytes TickIndex (int64) + 32 bytes StateHash = 40 bytes.
        const int entrySize = Int64Size + SHA256.HashSizeInBytes;
        var buffer = new byte[Int32Size + checkpoints.Count * entrySize];
        var offset = 0;

        BinaryPrimitives.WriteInt32BigEndian(buffer, checkpoints.Count);
        offset += Int32Size;

        foreach (var cp in checkpoints)
        {
            if (cp is null)
                throw new ArgumentException("Checkpoint entry cannot be null.", nameof(checkpoints));

            BinaryPrimitives.WriteInt64BigEndian(buffer.AsSpan(offset), cp.TickIndex);
            offset += Int64Size;

            // Validate and normalize the state hash to an exact 32-byte SHA-256 value.
            var stateHash = CloneAndValidateSha256Hash(cp.StateHash);
            stateHash.CopyTo(buffer, offset);
            offset += SHA256.HashSizeInBytes;
        }

        return SHA256.HashData(buffer);
    }

    /// <summary>
    /// Computes the signing payload hash for a run result that includes the final state hash,
    /// a replay hash, the Merkle root of all tick leaf hashes, and a hash of the checkpoints list.
    /// The signature covers:
    /// SessionId || PlayerId || FinalStateHash || ReplayHash || TickMerkleRoot || Hash(Checkpoints).
    /// </summary>
    internal static byte[] ComputeRunWithCheckpointsPayloadHash(
        Guid sessionId,
        Guid playerId,
        byte[] finalStateHash,
        byte[] replayHash,
        byte[] tickMerkleRoot,
        byte[] checkpointsHash)
    {
        var normalizedFinalStateHash = CloneAndValidateSha256Hash(finalStateHash);
        var normalizedReplayHash = CloneAndValidateSha256Hash(replayHash);
        var normalizedMerkleRoot = CloneAndValidateSha256Hash(tickMerkleRoot);
        var normalizedCheckpointsHash = CloneAndValidateSha256Hash(checkpointsHash);
        var buffer = new byte[
            GuidSize + GuidSize
            + Int32Size + normalizedFinalStateHash.Length
            + Int32Size + normalizedReplayHash.Length
            + Int32Size + normalizedMerkleRoot.Length
            + Int32Size + normalizedCheckpointsHash.Length];
        var offset = 0;

        WriteGuid(sessionId, buffer, ref offset);
        WriteGuid(playerId, buffer, ref offset);
        WriteLengthPrefixed(normalizedFinalStateHash, buffer, ref offset);
        WriteLengthPrefixed(normalizedReplayHash, buffer, ref offset);
        WriteLengthPrefixed(normalizedMerkleRoot, buffer, ref offset);
        WriteLengthPrefixed(normalizedCheckpointsHash, buffer, ref offset);

        return SHA256.HashData(buffer);
    }

    /// <summary>
    /// Computes the signing payload hash for a state snapshot.
    /// The canonical encoding (before hashing) is:
    /// <c>SessionId (16 bytes, big-endian) ||
    /// TickIndex (big-endian int64) ||
    /// len(StateHash) (big-endian int32) || StateHash ||
    /// len(SerializedState) (big-endian int32) || SerializedState</c>.
    /// The final hash is <c>SHA-256</c> over that buffer.
    /// </summary>
    internal static byte[] ComputeSnapshotPayloadHash(
        Guid sessionId,
        long tickIndex,
        byte[] stateHash,
        byte[] serializedState)
    {
        ArgumentNullException.ThrowIfNull(serializedState);
        var normalizedStateHash = CloneAndValidateSha256Hash(stateHash);
        var buffer = new byte[
            GuidSize
            + Int64Size
            + Int32Size + normalizedStateHash.Length
            + Int32Size + serializedState.Length];
        var offset = 0;

        WriteGuid(sessionId, buffer, ref offset);
        WriteInt64(tickIndex, buffer, ref offset);
        WriteLengthPrefixed(normalizedStateHash, buffer, ref offset);
        WriteLengthPrefixed(serializedState, buffer, ref offset);

        return SHA256.HashData(buffer);
    }

    /// <summary>
    /// Computes the signing payload hash for a run receipt.
    /// The canonical encoding (before hashing) is:
    /// <c>SessionId (16 bytes, big-endian) ||
    /// FinalTick (big-endian int64) ||
    /// len(FinalStateHash) (big-endian int32) || FinalStateHash ||
    /// len(TickMerkleRoot) (big-endian int32) || TickMerkleRoot</c>.
    /// The final hash is <c>SHA-256</c> over that buffer.
    /// </summary>
    internal static byte[] ComputeReceiptPayloadHash(
        Guid sessionId,
        long finalTick,
        byte[] finalStateHash,
        byte[] tickMerkleRoot)
    {
        var normalizedFinalStateHash = CloneAndValidateSha256Hash(finalStateHash);
        var normalizedTickMerkleRoot = CloneAndValidateSha256Hash(tickMerkleRoot);
        var buffer = new byte[
            GuidSize
            + Int64Size
            + Int32Size + normalizedFinalStateHash.Length
            + Int32Size + normalizedTickMerkleRoot.Length];
        var offset = 0;

        WriteGuid(sessionId, buffer, ref offset);
        WriteInt64(finalTick, buffer, ref offset);
        WriteLengthPrefixed(normalizedFinalStateHash, buffer, ref offset);
        WriteLengthPrefixed(normalizedTickMerkleRoot, buffer, ref offset);

        return SHA256.HashData(buffer);
    }

    /// <summary>
    /// Computes the signing payload hash for a <see cref="MerkleCheckpoint"/>.
    /// The canonical encoding (before hashing) is:
    /// <c>"checkpoint" (UTF-8, 10 bytes) || Tick (big-endian uint64) || MerkleRoot (32 bytes)</c>.
    /// The final hash is <c>SHA-256</c> over that buffer.
    /// </summary>
    internal static byte[] ComputeMerkleCheckpointPayloadHash(ulong tick, byte[] merkleRoot)
    {
        var normalizedRoot = CloneAndValidateSha256Hash(merkleRoot);
        // "checkpoint" in UTF-8 = 10 bytes.
        ReadOnlySpan<byte> prefix = "checkpoint"u8;
        const int uint64Size = sizeof(ulong);
        var buffer = new byte[prefix.Length + uint64Size + normalizedRoot.Length];
        var offset = 0;

        prefix.CopyTo(buffer.AsSpan(offset));
        offset += prefix.Length;

        BinaryPrimitives.WriteUInt64BigEndian(buffer.AsSpan(offset), tick);
        offset += uint64Size;

        normalizedRoot.CopyTo(buffer, offset);

        return SHA256.HashData(buffer);
    }

    /// <summary>
    /// Computes the deterministic leaf hash for a single signed tick.
    /// The canonical encoding (before hashing) is:
    /// <c>Tick (big-endian int64) || len(PrevStateHash) (big-endian int32) || PrevStateHash ||
    /// len(StateHash) (big-endian int32) || StateHash || len(InputHash) (big-endian int32) || InputHash</c>.
    /// The final leaf hash is <c>SHA-256</c> over that buffer.
    /// This encoding matches <see cref="ComputeTickPayloadHash"/> exactly and serves as the
    /// canonical leaf value for each tick's position in the Merkle tree.
    /// </summary>
    internal static byte[] ComputeTickLeafHash(
        long tick,
        byte[] prevStateHash,
        byte[] stateHash,
        byte[] inputHash) =>
        ComputeTickPayloadHash(tick, prevStateHash, stateHash, inputHash);

    /// <summary>
    /// Computes the signing payload hash for a per-tick signed tick.
    /// The canonical encoding (before hashing) is:
    /// <c>Tick (big-endian int64) || len(PrevStateHash) (big-endian int32) || PrevStateHash ||
    /// len(StateHash) (big-endian int32) || StateHash || len(InputHash) (big-endian int32) || InputHash</c>.
    /// The final hash is <c>SHA-256</c> over that buffer.
    /// Including <paramref name="prevStateHash"/> chains each signed tick to its predecessor,
    /// preventing valid ticks from being replayed or spliced from a different timeline.
    /// </summary>
    internal static byte[] ComputeTickPayloadHash(
        long tick,
        byte[] prevStateHash,
        byte[] stateHash,
        byte[] inputHash)
    {
        var normalizedPrev = CloneAndValidateSha256Hash(prevStateHash);
        var normalizedState = CloneAndValidateSha256Hash(stateHash);
        var normalizedInput = CloneAndValidateSha256Hash(inputHash);
        var buffer = new byte[
            Int64Size
            + Int32Size + normalizedPrev.Length
            + Int32Size + normalizedState.Length
            + Int32Size + normalizedInput.Length];
        var offset = 0;

        WriteInt64(tick, buffer, ref offset);
        WriteLengthPrefixed(normalizedPrev, buffer, ref offset);
        WriteLengthPrefixed(normalizedState, buffer, ref offset);
        WriteLengthPrefixed(normalizedInput, buffer, ref offset);

        return SHA256.HashData(buffer);
    }

    internal static byte[] ComputeRunResultHash(Guid runId, Guid playerId, Guid serverId, byte[] finalStateHash)
    {
        var normalizedFinalStateHash = CloneAndValidateSha256Hash(finalStateHash);
        var buffer = new byte[GuidSize + GuidSize + GuidSize + Int32Size + normalizedFinalStateHash.Length];
        var offset = 0;

        WriteGuid(runId, buffer, ref offset);
        WriteGuid(playerId, buffer, ref offset);
        WriteGuid(serverId, buffer, ref offset);
        WriteLengthPrefixed(normalizedFinalStateHash, buffer, ref offset);

        return SHA256.HashData(buffer);
    }

    internal static byte[] ComputeAuthorityEventHash(AuthorityEvent authorityEvent)
    {
        ArgumentNullException.ThrowIfNull(authorityEvent);

        return authorityEvent switch
        {
            AuthorityAdded added => ComputeAuthorityEventHash(1, added.PublicKeyBytes),
            AuthorityRemoved removed => ComputeAuthorityEventHash(2, removed.PublicKeyBytes),
            AuthorityRotated rotated => ComputeAuthorityEventHash(3, rotated.OldKeyBytes, rotated.NewKeyBytes),
            _ => throw new ArgumentException("Unsupported authority event type.", nameof(authorityEvent))
        };
    }

    internal static byte[] CloneAndValidatePublicKey(byte[] publicKey)
    {
        ArgumentNullException.ThrowIfNull(publicKey);
        if (publicKey.Length != KeySize)
        {
            throw new ArgumentException("Ed25519 public keys must be 32 bytes.", nameof(publicKey));
        }

        return (byte[])publicKey.Clone();
    }

    internal static byte[] CloneAndValidatePrivateKey(byte[] privateKey)
    {
        ArgumentNullException.ThrowIfNull(privateKey);
        if (privateKey.Length != KeySize)
        {
            throw new ArgumentException("Ed25519 private keys must be 32 bytes.", nameof(privateKey));
        }

        return (byte[])privateKey.Clone();
    }

    internal static byte[] CloneAndValidateSignature(byte[] signature)
    {
        ArgumentNullException.ThrowIfNull(signature);
        if (signature.Length != SignatureSize)
        {
            throw new ArgumentException("Ed25519 signatures must be 64 bytes.", nameof(signature));
        }

        return (byte[])signature.Clone();
    }

    internal static byte[] CloneAndValidateSha256Hash(byte[] value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Length != SHA256.HashSizeInBytes)
        {
            throw new ArgumentException($"SHA-256 hashes must be {SHA256.HashSizeInBytes} bytes.", nameof(value));
        }

        return (byte[])value.Clone();
    }

    private static void WriteGuid(Guid value, Span<byte> destination, ref int offset)
    {
        if (!value.TryWriteBytes(destination[offset..], bigEndian: true, out var bytesWritten) || bytesWritten != GuidSize)
        {
            throw new InvalidOperationException("Failed to write a 16-byte big-endian Guid into the signature payload buffer.");
        }

        offset += bytesWritten;
    }

    private static void WriteInt64(long value, Span<byte> destination, ref int offset)
    {
        BinaryPrimitives.WriteInt64BigEndian(destination[offset..], value);
        offset += Int64Size;
    }

    private static void WriteLengthPrefixed(byte[] value, Span<byte> destination, ref int offset)
    {
        BinaryPrimitives.WriteInt32BigEndian(destination[offset..], value.Length);
        offset += Int32Size;
        value.CopyTo(destination[offset..]);
        offset += value.Length;
    }

    private static byte[] ComputeAuthorityEventHash(int eventType, params byte[][] keys)
    {
        var buffer = new byte[Int32Size + (keys.Length * KeySize)];
        var offset = 0;
        BinaryPrimitives.WriteInt32BigEndian(buffer, eventType);
        offset += Int32Size;

        foreach (var key in keys)
        {
            var normalizedKey = CloneAndValidatePublicKey(key);
            normalizedKey.CopyTo(buffer, offset);
            offset += normalizedKey.Length;
        }

        return SHA256.HashData(buffer);
    }
}
