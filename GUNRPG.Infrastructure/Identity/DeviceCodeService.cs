using System.Security.Cryptography;
using System.Text;
using GUNRPG.Application.Identity;
using GUNRPG.Application.Identity.Dtos;
using GUNRPG.Application.Results;
using GUNRPG.Core.Identity;
using LiteDB;
using Microsoft.AspNetCore.Identity;

namespace GUNRPG.Infrastructure.Identity;

/// <summary>
/// Device Code Flow service for console clients.
/// Issues short-lived user codes, validates browser-side WebAuthn completion,
/// and lets the console poll for token issuance.
/// </summary>
public sealed class DeviceCodeService : IDeviceCodeService
{
    private static readonly TimeSpan DeviceCodeExpiry = TimeSpan.FromMinutes(15);

    private const int UserCodeLength = 8; // e.g. "WXYZ1234"

    private readonly ILiteCollection<DeviceCode> _codes;
    private readonly ITokenService _tokenService;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly string _verificationUri;

    public DeviceCodeService(
        ILiteDatabase db,
        ITokenService tokenService,
        UserManager<ApplicationUser> userManager,
        string verificationUri)
    {
        _codes = db.GetCollection<DeviceCode>("identity_device_codes");
        _codes.EnsureIndex(c => c.Code, unique: true);
        _codes.EnsureIndex(c => c.UserCode, unique: true);
        _tokenService = tokenService;
        _userManager = userManager;
        _verificationUri = verificationUri;
    }

    public Task<DeviceCodeResponse> StartAsync(CancellationToken ct = default)
    {
        PurgeExpired();

        var deviceCode = GenerateDeviceCode();
        var userCode = GenerateUserCode();

        var code = new DeviceCode
        {
            Code = deviceCode,
            UserCode = userCode,
            VerificationUri = _verificationUri,
            IssuedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.Add(DeviceCodeExpiry),
        };

        _codes.Insert(code);

        return Task.FromResult(new DeviceCodeResponse(
            DeviceCode: deviceCode,
            UserCode: userCode,
            VerificationUri: _verificationUri,
            ExpiresInSeconds: (int)DeviceCodeExpiry.TotalSeconds,
            PollIntervalSeconds: code.PollIntervalSeconds));
    }

    public Task<ServiceResult> AuthorizeAsync(string userCode, string userId, CancellationToken ct = default)
    {
        var normalised = userCode.ToUpperInvariant().Replace("-", "").Replace(" ", "");
        var code = _codes.FindOne(c => c.UserCode == normalised || c.UserCode == userCode);

        if (code is null)
            return Task.FromResult(ServiceResult.NotFound("User code not found."));

        if (code.IsExpired)
        {
            _codes.Delete(code.Id);
            return Task.FromResult(ServiceResult.InvalidState("Device code has expired."));
        }

        if (code.IsAuthorized)
            return Task.FromResult(ServiceResult.InvalidState("Device code is already authorized."));

        code.AuthorizedUserId = userId;
        _codes.Update(code);

        return Task.FromResult(ServiceResult.Success());
    }

    public async Task<ServiceResult<DevicePollResponse>> PollAsync(string deviceCode, CancellationToken ct = default)
    {
        var code = _codes.FindOne(c => c.Code == deviceCode);

        if (code is null)
            return ServiceResult<DevicePollResponse>.NotFound("Device code not found.");

        if (code.IsExpired)
        {
            _codes.Delete(code.Id);
            return ServiceResult<DevicePollResponse>.Success(new DevicePollResponse("expired_token", null));
        }

        // Rate limiting: enforce minimum poll interval (RFC 8628 §3.5 "slow_down")
        if (code.LastPolledAt.HasValue)
        {
            var elapsed = DateTimeOffset.UtcNow - code.LastPolledAt.Value;
            if (elapsed.TotalSeconds < code.PollIntervalSeconds)
                return ServiceResult<DevicePollResponse>.Success(new DevicePollResponse("slow_down", null));
        }

        code.LastPolledAt = DateTimeOffset.UtcNow;
        _codes.Update(code);

        if (!code.IsAuthorized)
            return ServiceResult<DevicePollResponse>.Success(new DevicePollResponse("authorization_pending", null));

        // Authorization granted — issue tokens and consume the code
        var user = await _userManager.FindByIdAsync(code.AuthorizedUserId!);
        if (user is null)
            return ServiceResult<DevicePollResponse>.InvalidState("Authorized user no longer exists.");

        var accountProvisioning = await AccountIdProvisioning.EnsureAssignedAsync(_userManager, user, ct);
        if (!accountProvisioning.Succeeded)
            return ServiceResult<DevicePollResponse>.InvalidState(
                string.Join("; ", accountProvisioning.Errors.Select(e => e.Description)));

        _codes.Delete(code.Id);

        var tokens = await _tokenService.IssueTokensAsync(user.Id, user.UserName, user.AccountId, ct);
        return ServiceResult<DevicePollResponse>.Success(new DevicePollResponse("authorized", tokens));
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static string GenerateDeviceCode()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static string GenerateUserCode()
    {
        // Generate an 8-character alphanumeric code (uppercase only, no ambiguous chars)
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        var bytes = RandomNumberGenerator.GetBytes(UserCodeLength);
        var sb = new StringBuilder(UserCodeLength);
        foreach (var b in bytes)
            sb.Append(chars[b % chars.Length]);
        return sb.ToString();
    }

    private void PurgeExpired()
    {
        var now = DateTimeOffset.UtcNow;
        _codes.DeleteMany(c => c.ExpiresAt <= now);
    }
}
