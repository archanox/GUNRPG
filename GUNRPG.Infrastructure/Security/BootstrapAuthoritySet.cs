namespace GUNRPG.Security;

public sealed class BootstrapAuthoritySet
{
    private readonly HashSet<string> _allowedKeys;

    public BootstrapAuthoritySet(IEnumerable<Authority> authorities)
    {
        ArgumentNullException.ThrowIfNull(authorities);

        _allowedKeys = new HashSet<string>(
            authorities.Select(static authority =>
            {
                ArgumentNullException.ThrowIfNull(authority);
                return CreateKeyIdentifier(authority.PublicKeyBytes);
            }),
            StringComparer.Ordinal);
    }

    public bool IsTrusted(byte[] publicKey)
    {
        return _allowedKeys.Contains(CreateKeyIdentifier(publicKey));
    }

    internal IEnumerable<string> KeyIdentifiers => _allowedKeys;

    internal static string CreateKeyIdentifier(byte[] publicKey)
    {
        return Convert.ToHexString(AuthorityCrypto.CloneAndValidatePublicKey(publicKey));
    }
}
