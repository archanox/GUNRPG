using System.Security.Cryptography;

namespace GUNRPG.Security;

public abstract class AuthorityEvent;

public sealed class AuthorityAdded : AuthorityEvent, IEquatable<AuthorityAdded>
{
    private readonly byte[] _publicKey;

    public AuthorityAdded(byte[] publicKey)
    {
        _publicKey = AuthorityCrypto.CloneAndValidatePublicKey(publicKey);
    }

    public byte[] PublicKey => (byte[])_publicKey.Clone();

    internal byte[] PublicKeyBytes => _publicKey;

    public bool Equals(AuthorityAdded? other)
    {
        return other is not null
            && CryptographicOperations.FixedTimeEquals(_publicKey, other._publicKey);
    }

    public override bool Equals(object? obj) => obj is AuthorityAdded other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.AddBytes(_publicKey);
        return hash.ToHashCode();
    }
}

public sealed class AuthorityRemoved : AuthorityEvent, IEquatable<AuthorityRemoved>
{
    private readonly byte[] _publicKey;

    public AuthorityRemoved(byte[] publicKey)
    {
        _publicKey = AuthorityCrypto.CloneAndValidatePublicKey(publicKey);
    }

    public byte[] PublicKey => (byte[])_publicKey.Clone();

    internal byte[] PublicKeyBytes => _publicKey;

    public bool Equals(AuthorityRemoved? other)
    {
        return other is not null
            && CryptographicOperations.FixedTimeEquals(_publicKey, other._publicKey);
    }

    public override bool Equals(object? obj) => obj is AuthorityRemoved other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.AddBytes(_publicKey);
        return hash.ToHashCode();
    }
}

public sealed class AuthorityRotated : AuthorityEvent, IEquatable<AuthorityRotated>
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

    public bool Equals(AuthorityRotated? other)
    {
        return other is not null
            && CryptographicOperations.FixedTimeEquals(_oldKey, other._oldKey)
            && CryptographicOperations.FixedTimeEquals(_newKey, other._newKey);
    }

    public override bool Equals(object? obj) => obj is AuthorityRotated other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.AddBytes(_oldKey);
        hash.AddBytes(_newKey);
        return hash.ToHashCode();
    }
}
