using System.Buffers.Binary;
using Google.Protobuf;
using Nethermind.Libp2p.Core;
using Nethermind.Libp2p.Core.Dto;
using System.Security.Cryptography;

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
        return CreatePrivateIdentity(normalizedPrivateKey).PublicKey.Data.ToByteArray();
    }

    internal static byte[] SignPayload(byte[] privateKey, byte[] payload)
    {
        var normalizedPrivateKey = CloneAndValidatePrivateKey(privateKey);
        ArgumentNullException.ThrowIfNull(payload);

        return CreatePrivateIdentity(normalizedPrivateKey).Sign(payload);
    }

    internal static bool VerifyPayload(byte[] publicKey, byte[] payload, byte[] signature)
    {
        var normalizedPublicKey = CloneAndValidatePublicKey(publicKey);
        ArgumentNullException.ThrowIfNull(payload);
        var normalizedSignature = CloneAndValidateSignature(signature);

        return CreatePublicIdentity(normalizedPublicKey).VerifySignature(payload, normalizedSignature);
    }

    internal static byte[] SignHashedPayload(byte[] privateKey, byte[] payloadHash)
    {
        var normalizedHash = CloneAndValidateHash(payloadHash);
        return SignPayload(privateKey, normalizedHash);
    }

    internal static bool VerifyHashedPayload(byte[] publicKey, byte[] payloadHash, byte[] signature)
    {
        var normalizedHash = CloneAndValidateHash(payloadHash);
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
        var normalizedFinalStateHash = CloneAndValidateHash(finalStateHash);
        var buffer = new byte[GuidSize + GuidSize + Int32Size + normalizedFinalStateHash.Length];
        var offset = 0;

        WriteGuid(runId, buffer, ref offset);
        WriteGuid(playerId, buffer, ref offset);
        WriteLengthPrefixed(normalizedFinalStateHash, buffer, ref offset);

        return SHA256.HashData(buffer);
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

    internal static byte[] CloneAndValidateHash(byte[] value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Length == 0)
        {
            throw new ArgumentException("Hash payloads must not be empty.", nameof(value));
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

    private static Identity CreatePrivateIdentity(byte[] privateKey) =>
        new(privateKey, KeyType.Ed25519);

    private static Identity CreatePublicIdentity(byte[] publicKey) =>
        new(new PublicKey
        {
            Type = KeyType.Ed25519,
            Data = ByteString.CopyFrom(publicKey),
        });
}
