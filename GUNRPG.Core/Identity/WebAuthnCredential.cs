namespace GUNRPG.Core.Identity;

/// <summary>
/// Persisted WebAuthn credential for a registered authenticator (e.g. YubiKey).
/// Tracks the public key, signature counter, and transport hints for each credential.
/// </summary>
public sealed class WebAuthnCredential
{
    /// <summary>Credential ID as issued by the authenticator (base64url-encoded bytes).</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>The ASP.NET Identity user that owns this credential.</summary>
    public string UserId { get; set; } = string.Empty;

    /// <summary>COSE-encoded public key returned during registration.</summary>
    public byte[] PublicKey { get; set; } = [];

    /// <summary>
    /// Signature counter last seen for this credential.
    /// Must be greater than the stored value on every authentication to prevent replay attacks.
    /// </summary>
    public uint SignatureCounter { get; set; }

    /// <summary>Human-readable nickname for the authenticator (e.g. "YubiKey 5").</summary>
    public string? Nickname { get; set; }

    /// <summary>AAGUID that identifies the authenticator model.</summary>
    public Guid AaGuid { get; set; }

    /// <summary>Transports advertised by the authenticator (e.g. "usb", "nfc", "ble").</summary>
    public List<string> Transports { get; set; } = [];

    public DateTimeOffset RegisteredAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? LastUsedAt { get; set; }
}
