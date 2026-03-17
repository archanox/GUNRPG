namespace GUNRPG.Security;

public sealed record AuthoritySignature
{
    private readonly byte[] _publicKey;
    private readonly byte[] _signature;

    public AuthoritySignature(byte[] publicKey, byte[] signature)
    {
        _publicKey = AuthorityCrypto.CloneAndValidatePublicKey(publicKey);
        _signature = AuthorityCrypto.CloneAndValidateSignature(signature);
    }

    public byte[] PublicKey => (byte[])_publicKey.Clone();

    internal byte[] PublicKeyBytes => _publicKey;

    public byte[] Signature => (byte[])_signature.Clone();

    internal byte[] SignatureBytes => _signature;
}
