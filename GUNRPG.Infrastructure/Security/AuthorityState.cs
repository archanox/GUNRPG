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
        return _activeAuthorities.Contains(BootstrapAuthoritySet.CreateKeyIdentifier(publicKey));
    }

    public static AuthorityState BuildFromLedger(RunLedger ledger)
    {
        ArgumentNullException.ThrowIfNull(ledger);
        var state = ledger.GetBootstrapAuthorityState();
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

    internal bool IsEquivalentTo(AuthorityState other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return _activeAuthorities.SetEquals(other._activeAuthorities);
    }

    internal AuthorityState Apply(AuthorityEvent authorityEvent)
    {
        ArgumentNullException.ThrowIfNull(authorityEvent);

        var next = new HashSet<string>(_activeAuthorities, StringComparer.Ordinal);
        switch (authorityEvent)
        {
            case AuthorityAdded added:
                next.Add(BootstrapAuthoritySet.CreateKeyIdentifier(added.PublicKeyBytes));
                break;
            case AuthorityRemoved removed:
                next.Remove(BootstrapAuthoritySet.CreateKeyIdentifier(removed.PublicKeyBytes));
                break;
            case AuthorityRotated rotated:
                next.Remove(BootstrapAuthoritySet.CreateKeyIdentifier(rotated.OldKeyBytes));
                next.Add(BootstrapAuthoritySet.CreateKeyIdentifier(rotated.NewKeyBytes));
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
            yield return BootstrapAuthoritySet.CreateKeyIdentifier(authority.PublicKeyBytes);
        }
    }
}
