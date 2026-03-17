namespace GUNRPG.Security;

public sealed class AuthoritySet
{
    private readonly HashSet<string> _allowedKeys;

    public AuthoritySet(IEnumerable<Authority> authorities)
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

    internal static string CreateKeyIdentifier(byte[] publicKey)
    {
        return Convert.ToHexString(AuthorityCrypto.CloneAndValidatePublicKey(publicKey));
    }
}
