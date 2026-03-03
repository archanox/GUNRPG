namespace GUNRPG.Core.Identity;

/// <summary>
/// Represents a pending Device Code flow session for console clients.
/// The console displays a short user code; the user visits the verification URI
/// and completes WebAuthn in the browser; the console polls for completion.
/// </summary>
public sealed class DeviceCode
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Opaque device code returned to the console client.
    /// Long random value — never shown to the user.
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// Short user-friendly code the user enters at the verification URI (e.g. "WXYZ-1234").
    /// </summary>
    public string UserCode { get; set; } = string.Empty;

    /// <summary>Verification URI the user navigates to in their browser.</summary>
    public string VerificationUri { get; set; } = string.Empty;

    public DateTimeOffset IssuedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>
    /// Set once the user successfully authenticates in the browser.
    /// The polling console client exchanges this for a token pair.
    /// </summary>
    public string? AuthorizedUserId { get; set; }

    public bool IsAuthorized => AuthorizedUserId is not null;

    public bool IsExpired => ExpiresAt <= DateTimeOffset.UtcNow;

    /// <summary>
    /// Minimum seconds the console must wait between poll requests (rate limiting).
    /// </summary>
    public int PollIntervalSeconds { get; set; } = 5;

    /// <summary>Timestamp of the most recent poll — used to enforce the poll interval.</summary>
    public DateTimeOffset? LastPolledAt { get; set; }
}
