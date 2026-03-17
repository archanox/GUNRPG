namespace GUNRPG.Security;

public sealed record Authority
{
    private readonly byte[] _publicKey;

    public Authority(byte[] publicKey, string id)
    {
        _publicKey = AuthorityCrypto.CloneAndValidatePublicKey(publicKey);
        Id = string.IsNullOrWhiteSpace(id)
            ? throw new ArgumentException("Authority id must not be empty.", nameof(id))
            : id;
    }

    public byte[] PublicKey => (byte[])_publicKey.Clone();

    internal byte[] PublicKeyBytes => _publicKey;

    public string Id { get; }
}
