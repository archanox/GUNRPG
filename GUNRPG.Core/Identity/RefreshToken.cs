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

    public DateTimeOffset IssuedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>Whether this token has already been exchanged for a new pair.</summary>
    public bool IsConsumed { get; set; }

    /// <summary>Whether this token was explicitly revoked (logout / security event).</summary>
    public bool IsRevoked { get; set; }

    public bool IsActive => !IsConsumed && !IsRevoked && ExpiresAt > DateTimeOffset.UtcNow;
}
