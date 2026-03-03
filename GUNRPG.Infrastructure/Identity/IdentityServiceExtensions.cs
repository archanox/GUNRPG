using Fido2NetLib;
using GUNRPG.Application.Identity;
using GUNRPG.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace GUNRPG.Infrastructure;

/// <summary>
/// Extension methods for registering the GunRPG identity system (ASP.NET Identity + WebAuthn + JWT).
/// </summary>
public static class IdentityServiceExtensions
{
    /// <summary>
    /// Registers all identity infrastructure services:
    /// ASP.NET Identity backed by LiteDB, JWT token issuance (Ed25519), WebAuthn (Fido2NetLib),
    /// and Device Code Flow for console clients.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="verificationUri">
    ///     The publicly reachable URI users visit to complete device code authorization.
    ///     Must be HTTPS for WebAuthn.  Example: "https://yourdomain/auth/device/verify"
    /// </param>
    public static IServiceCollection AddGunRpgIdentity(
        this IServiceCollection services,
        string verificationUri)
    {
        // ── ASP.NET Identity ─────────────────────────────────────────────────
        services
            .AddIdentityCore<ApplicationUser>(opts =>
            {
                // WebAuthn users have no password; disable password requirements.
                opts.Password.RequireDigit = false;
                opts.Password.RequireLowercase = false;
                opts.Password.RequireUppercase = false;
                opts.Password.RequireNonAlphanumeric = false;
                opts.Password.RequiredLength = 0;
            })
            .AddUserStore<LiteDbUserStore>();

        // ── WebAuthn storage ─────────────────────────────────────────────────
        services.AddSingleton<LiteDbWebAuthnStore>();

        // ── Fido2NetLib ──────────────────────────────────────────────────────
        services.AddSingleton<IFido2>(sp =>
        {
            var cfg = sp.GetRequiredService<IOptions<Fido2Configuration>>().Value;
            return new Fido2(cfg);
        });

        // ── JWT token service (Ed25519 via BouncyCastle) ─────────────────────
        services.AddSingleton<JwtTokenService>();
        services.AddSingleton<ITokenService>(sp => sp.GetRequiredService<JwtTokenService>());

        // ── WebAuthn service ─────────────────────────────────────────────────
        services.AddSingleton<IWebAuthnService, WebAuthnService>();

        // ── Device Code service ──────────────────────────────────────────────
        services.AddSingleton<IDeviceCodeService>(sp =>
            new DeviceCodeService(
                sp.GetRequiredService<LiteDB.ILiteDatabase>(),
                sp.GetRequiredService<ITokenService>(),
                sp.GetRequiredService<UserManager<ApplicationUser>>(),
                verificationUri));

        return services;
    }
}
