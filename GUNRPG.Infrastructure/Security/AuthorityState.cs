using GUNRPG.Ledger;

namespace GUNRPG.Security;

public sealed class AuthorityState
{
    private readonly HashSet<string> _activeAuthorities;

    public AuthorityState(IEnumerable<Authority> authorities)
        : this(CreateIdentifiers(authorities))
    {
    }

    internal AuthorityState(IEnumerable<string> authorityIdentifiers)
    {
        ArgumentNullException.ThrowIfNull(authorityIdentifiers);
        _activeAuthorities = new HashSet<string>(authorityIdentifiers, StringComparer.Ordinal);
    }

    public int Count => _activeAuthorities.Count;

    public bool IsTrusted(byte[] publicKey)
    {
        return _activeAuthorities.Contains(AuthoritySet.CreateKeyIdentifier(publicKey));
    }

    public static AuthorityState BuildFromLedger(RunLedger ledger)
    {
        ArgumentNullException.ThrowIfNull(ledger);
        return BuildFromLedger(ledger, fallbackBootstrapAuthorities: null);
    }

    internal static AuthorityState BuildFromLedger(
        RunLedger ledger,
        AuthoritySet? fallbackBootstrapAuthorities)
    {
        ArgumentNullException.ThrowIfNull(ledger);

        var state = ledger.GetBootstrapAuthorityState(fallbackBootstrapAuthorities);
        foreach (var entry in ledger.Entries)
        {
            if (entry.AuthorityEvent is null)
            {
                continue;
            }

            state = state.Apply(entry.AuthorityEvent);
        }

        return state;
    }

    internal AuthorityState Apply(AuthorityEvent authorityEvent)
    {
        ArgumentNullException.ThrowIfNull(authorityEvent);

        var next = new HashSet<string>(_activeAuthorities, StringComparer.Ordinal);
        switch (authorityEvent)
        {
            case AuthorityAdded added:
                next.Add(AuthoritySet.CreateKeyIdentifier(added.PublicKeyBytes));
                break;
            case AuthorityRemoved removed:
                next.Remove(AuthoritySet.CreateKeyIdentifier(removed.PublicKeyBytes));
                break;
            case AuthorityRotated rotated:
                next.Remove(AuthoritySet.CreateKeyIdentifier(rotated.OldKeyBytes));
                next.Add(AuthoritySet.CreateKeyIdentifier(rotated.NewKeyBytes));
                break;
            default:
                throw new ArgumentException("Unsupported authority event type.", nameof(authorityEvent));
        }

        return new AuthorityState(next);
    }

    internal AuthorityState Clone() => new(_activeAuthorities);

    private static IEnumerable<string> CreateIdentifiers(IEnumerable<Authority> authorities)
    {
        ArgumentNullException.ThrowIfNull(authorities);

        foreach (var authority in authorities)
        {
            ArgumentNullException.ThrowIfNull(authority);
            yield return AuthoritySet.CreateKeyIdentifier(authority.PublicKeyBytes);
        }
    }
}
