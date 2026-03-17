namespace GUNRPG.Security;

public sealed class ServerCertificate
{
    private readonly byte[] _publicKey;
    private readonly byte[] _signature;

    public ServerCertificate(
        Guid serverId,
        byte[] publicKey,
        DateTimeOffset issuedAt,
        DateTimeOffset validUntil,
        byte[] signature)
    {
        if (validUntil <= issuedAt)
        {
            throw new ArgumentOutOfRangeException(nameof(validUntil), "Certificate expiry must be after issuance.");
        }

        ServerId = serverId;
        _publicKey = AuthorityCrypto.CloneAndValidatePublicKey(publicKey);
        IssuedAt = issuedAt;
        ValidUntil = validUntil;
        _signature = AuthorityCrypto.CloneAndValidateSignature(signature);
    }

    public Guid ServerId { get; }

    public byte[] PublicKey => (byte[])_publicKey.Clone();

    internal byte[] PublicKeyBytes => _publicKey;

    public DateTimeOffset IssuedAt { get; }

    public DateTimeOffset ValidUntil { get; }

    public byte[] Signature => (byte[])_signature.Clone();

    internal byte[] SignatureBytes => _signature;

    internal byte[] ComputePayloadHash() =>
        AuthorityCrypto.ComputeCertificatePayloadHash(ServerId, _publicKey, IssuedAt, ValidUntil);

    internal static ServerCertificate Create(
        Guid serverId,
        byte[] publicKey,
        DateTimeOffset issuedAt,
        DateTimeOffset validUntil,
        byte[] rootPrivateKey)
    {
        var payloadHash = AuthorityCrypto.ComputeCertificatePayloadHash(serverId, publicKey, issuedAt, validUntil);
        var signature = AuthorityCrypto.SignHashedPayload(rootPrivateKey, payloadHash);
        return new ServerCertificate(serverId, publicKey, issuedAt, validUntil, signature);
    }
}
