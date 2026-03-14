using System.Buffers.Binary;
using System.Security.Cryptography;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.Security;

namespace GUNRPG.Security;

public sealed class AuthorityRoot
{
    private readonly byte[] _publicKey;

    public AuthorityRoot(byte[] publicKey)
    {
        _publicKey = AuthorityCrypto.CloneAndValidatePublicKey(publicKey);
    }

    public byte[] PublicKey => (byte[])_publicKey.Clone();

    public bool VerifyServerCertificate(ServerCertificate cert)
    {
        ArgumentNullException.ThrowIfNull(cert);

        var now = DateTimeOffset.UtcNow;
        if (cert.ValidUntil <= cert.IssuedAt || cert.IssuedAt > now || cert.ValidUntil < now)
        {
            return false;
        }

        return AuthorityCrypto.VerifyHashedPayload(
            _publicKey,
            cert.ComputePayloadHash(),
            cert.Signature);
    }

    public ServerCertificate IssueServerCertificate(
        Guid serverId,
        byte[] serverPublicKey,
        DateTimeOffset issuedAt,
        DateTimeOffset validUntil,
        byte[] rootPrivateKey)
    {
        ArgumentNullException.ThrowIfNull(rootPrivateKey);

        var derivedPublicKey = AuthorityCrypto.GetPublicKey(rootPrivateKey);
        if (!CryptographicOperations.FixedTimeEquals(_publicKey, derivedPublicKey))
        {
            throw new ArgumentException("The supplied root private key does not match this authority root.", nameof(rootPrivateKey));
        }

        return ServerCertificate.Create(serverId, serverPublicKey, issuedAt, validUntil, rootPrivateKey);
    }

    public static byte[] GeneratePrivateKey() => AuthorityCrypto.GeneratePrivateKey();

    public static byte[] GetPublicKey(byte[] privateKey) => AuthorityCrypto.GetPublicKey(privateKey);
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
        var generator = new Ed25519KeyPairGenerator();
        generator.Init(new Ed25519KeyGenerationParameters(new SecureRandom()));
        var keyPair = generator.GenerateKeyPair();
        return ((Ed25519PrivateKeyParameters)keyPair.Private).GetEncoded();
    }

    internal static byte[] GetPublicKey(byte[] privateKey)
    {
        var normalizedPrivateKey = CloneAndValidatePrivateKey(privateKey);
        return new Ed25519PrivateKeyParameters(normalizedPrivateKey).GeneratePublicKey().GetEncoded();
    }

    internal static byte[] SignHashedPayload(byte[] privateKey, byte[] payloadHash)
    {
        var normalizedPrivateKey = CloneAndValidatePrivateKey(privateKey);
        var normalizedHash = CloneAndValidateHash(payloadHash);

        var signer = new Ed25519Signer();
        signer.Init(true, new Ed25519PrivateKeyParameters(normalizedPrivateKey));
        signer.BlockUpdate(normalizedHash, 0, normalizedHash.Length);
        return signer.GenerateSignature();
    }

    internal static bool VerifyHashedPayload(byte[] publicKey, byte[] payloadHash, byte[] signature)
    {
        var normalizedPublicKey = CloneAndValidatePublicKey(publicKey);
        var normalizedHash = CloneAndValidateHash(payloadHash);
        var normalizedSignature = CloneAndValidateSignature(signature);

        var verifier = new Ed25519Signer();
        verifier.Init(false, new Ed25519PublicKeyParameters(normalizedPublicKey));
        verifier.BlockUpdate(normalizedHash, 0, normalizedHash.Length);
        return verifier.VerifySignature(normalizedSignature);
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
}
