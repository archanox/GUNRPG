namespace GUNRPG.Infrastructure.Identity;

/// <summary>
/// Configuration for JWT token issuance.
/// Bind from appsettings.json under the "Jwt" section.
/// </summary>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = "gunrpg";

    public string Audience { get; set; } = "gunrpg";

    /// <summary>Lifetime of JWT access tokens in minutes. Default: 15 minutes.</summary>
    public int AccessTokenExpiryMinutes { get; set; } = 15;

    /// <summary>Lifetime of refresh tokens in days. Default: 30 days.</summary>
    public int RefreshTokenExpiryDays { get; set; } = 30;
}
