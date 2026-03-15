using System.Security.Cryptography;

namespace GUNRPG.Security;

public sealed class CertificateIssuer
{
    private readonly byte[] _rootPrivateKey;
    private readonly byte[] _rootPublicKey;

    public CertificateIssuer(byte[] rootPrivateKey)
    {
        _rootPrivateKey = AuthorityCrypto.CloneAndValidatePrivateKey(rootPrivateKey);
        _rootPublicKey = AuthorityCrypto.GetPublicKey(_rootPrivateKey);
    }

    public byte[] RootPublicKey => (byte[])_rootPublicKey.Clone();

    public ServerCertificate IssueServerCertificate(
        Guid serverId,
        byte[] serverPublicKey,
        DateTimeOffset issuedAt,
        DateTimeOffset validUntil)
    {
        return ServerCertificate.Create(serverId, serverPublicKey, issuedAt, validUntil, _rootPrivateKey);
    }

    public bool Matches(AuthorityRoot authorityRoot)
    {
        ArgumentNullException.ThrowIfNull(authorityRoot);
        return CryptographicOperations.FixedTimeEquals(_rootPublicKey, authorityRoot.PublicKey);
    }

    public static byte[] GeneratePrivateKey() => AuthorityCrypto.GeneratePrivateKey();

    public static byte[] GetPublicKey(byte[] privateKey) => AuthorityCrypto.GetPublicKey(privateKey);
}
