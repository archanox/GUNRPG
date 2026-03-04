using GUNRPG.Core.Identity;
using LiteDB;

namespace GUNRPG.Infrastructure.Identity;

/// <summary>
/// LiteDB-backed store for WebAuthn credentials and pending challenges.
/// Challenges are stored in-memory (short-lived) and referenced by username.
/// </summary>
public sealed class LiteDbWebAuthnStore
{
    private const string CredentialCollection = "webauthn_credentials";
    private const string AccountCollection = "identity_accounts";

    private readonly ILiteCollection<WebAuthnCredential> _credentials;
    private readonly ILiteCollection<Account> _accounts;

    // In-memory challenge cache: username → base64url challenge bytes
    // Challenges are short-lived (< 2 min) so in-memory storage is appropriate.
    private readonly Dictionary<string, (byte[] Challenge, DateTimeOffset Expires)> _challenges = new();
    private readonly Lock _challengeLock = new();

    public LiteDbWebAuthnStore(ILiteDatabase db)
    {
        _credentials = db.GetCollection<WebAuthnCredential>(CredentialCollection);
        _credentials.EnsureIndex(c => c.UserId);

        _accounts = db.GetCollection<Account>(AccountCollection);
        _accounts.EnsureIndex(a => a.UserId, unique: true);
    }

    // ── Challenges ──────────────────────────────────────────────────────────

    public void StoreChallenge(string username, byte[] challenge)
    {
        lock (_challengeLock)
        {
            PurgeExpiredChallenges();
            _challenges[username] = (challenge, DateTimeOffset.UtcNow.AddMinutes(2));
        }
    }

    public byte[]? ConsumeChallenge(string username)
    {
        lock (_challengeLock)
        {
            PurgeExpiredChallenges();
            if (!_challenges.TryGetValue(username, out var entry)) return null;
            _challenges.Remove(username);
            return entry.Challenge;
        }
    }

    private void PurgeExpiredChallenges()
    {
        var now = DateTimeOffset.UtcNow;
        var expired = _challenges
            .Where(kv => kv.Value.Expires <= now)
            .Select(kv => kv.Key)
            .ToList();
        foreach (var key in expired)
            _challenges.Remove(key);
    }

    // ── Credentials ─────────────────────────────────────────────────────────

    public void UpsertCredential(WebAuthnCredential credential) =>
        _credentials.Upsert(credential);

    public IEnumerable<WebAuthnCredential> GetCredentialsByUserId(string userId) =>
        _credentials.Find(c => c.UserId == userId);

    public WebAuthnCredential? GetCredentialById(string credentialId) =>
        _credentials.FindOne(c => c.Id == credentialId);

    // ── Accounts ────────────────────────────────────────────────────────────

    public void UpsertAccount(Account account) =>
        _accounts.Upsert(account);

    public Account? GetAccountByUserId(string userId) =>
        _accounts.FindOne(a => a.UserId == userId);

    public Account? GetAccountById(Guid accountId) =>
        _accounts.FindById(accountId);
}
