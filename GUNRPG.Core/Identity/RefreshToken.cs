namespace GUNRPG.Core.Identity;

/// <summary>
/// Persisted refresh token used for JWT rotation.
/// On every refresh the old token is consumed and a new one is issued.
/// </summary>
public sealed class RefreshToken
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>The ASP.NET Identity user this token belongs to.</summary>
    public string UserId { get; set; } = string.Empty;

    /// <summary>Cryptographically random token value (URL-safe base64).</summary>
    public string Token { get; set; } = string.Empty;

    /// <summary>
    /// Username claim persisted alongside the refresh token so that refreshed access tokens
    /// carry the same <c>preferred_username</c> claim as the originally issued token.
    /// </summary>
    public string? Username { get; set; }

    /// <summary>
    /// Account ID claim persisted alongside the refresh token so that refreshed access tokens
    /// carry the same <c>account_id</c> claim as the originally issued token.
    /// </summary>
    public Guid? AccountId { get; set; }

    public DateTimeOffset IssuedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>Whether this token has already been exchanged for a new pair.</summary>
    public bool IsConsumed { get; set; }

    /// <summary>Whether this token was explicitly revoked (logout / security event).</summary>
    public bool IsRevoked { get; set; }

    /// <summary>
    /// The token value that replaced this one during rotation.
    /// Provides an audit trail and supports detecting token-reuse attacks:
    /// if a consumed token is presented again, the entire family can be revoked.
    /// </summary>
    public string? ReplacedByToken { get; set; }

    public bool IsActive => !IsConsumed && !IsRevoked && ExpiresAt > DateTimeOffset.UtcNow;
}
