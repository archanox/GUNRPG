namespace GUNRPG.Infrastructure.Identity;

/// <summary>
/// Strongly-typed options for WebAuthn (Fido2) configuration.
/// Bound from appsettings.json under the "WebAuthn" section.
/// Maps to Fido2NetLib's <c>Fido2Configuration</c>.
/// </summary>
public sealed class WebAuthnOptions
{
    public const string SectionName = "WebAuthn";

    /// <summary>
    /// The Relying Party (RP) domain. Must match the effective domain of the server.
    /// For HTTPS origins this is the hostname without scheme or port.
    /// Example: "gunrpg.example.com"
    /// </summary>
    public string ServerDomain { get; set; } = "localhost";

    /// <summary>Friendly name of the RP shown by the authenticator UI.</summary>
    public string ServerName { get; set; } = "GunRPG";

    /// <summary>
    /// Allowed origins for WebAuthn ceremonies.
    /// Must include the exact origins of the web client (GitHub Pages) and any local dev URLs.
    /// Example: ["https://username.github.io", "https://localhost:5001"]
    /// </summary>
    public HashSet<string> Origins { get; set; } = ["https://localhost"];
}
