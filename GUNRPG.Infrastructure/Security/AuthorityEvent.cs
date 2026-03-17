namespace GUNRPG.Security;

public abstract record AuthorityEvent;

public sealed record AuthorityAdded : AuthorityEvent
{
    private readonly byte[] _publicKey;

    public AuthorityAdded(byte[] publicKey)
    {
        _publicKey = AuthorityCrypto.CloneAndValidatePublicKey(publicKey);
    }

    public byte[] PublicKey => (byte[])_publicKey.Clone();

    internal byte[] PublicKeyBytes => _publicKey;
}

public sealed record AuthorityRemoved : AuthorityEvent
{
    private readonly byte[] _publicKey;

    public AuthorityRemoved(byte[] publicKey)
    {
        _publicKey = AuthorityCrypto.CloneAndValidatePublicKey(publicKey);
    }

    public byte[] PublicKey => (byte[])_publicKey.Clone();

    internal byte[] PublicKeyBytes => _publicKey;
}

public sealed record AuthorityRotated : AuthorityEvent
{
    private readonly byte[] _oldKey;
    private readonly byte[] _newKey;

    public AuthorityRotated(byte[] oldKey, byte[] newKey)
    {
        _oldKey = AuthorityCrypto.CloneAndValidatePublicKey(oldKey);
        _newKey = AuthorityCrypto.CloneAndValidatePublicKey(newKey);
    }

    public byte[] OldKey => (byte[])_oldKey.Clone();

    internal byte[] OldKeyBytes => _oldKey;

    public byte[] NewKey => (byte[])_newKey.Clone();

    internal byte[] NewKeyBytes => _newKey;
}
