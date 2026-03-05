using System.Text;
using System.Text.Json;
using GUNRPG.Application.Identity;
using GUNRPG.Application.Identity.Dtos;
using GUNRPG.Application.Results;
using GUNRPG.Core.Identity;
using Fido2NetLib;
using Fido2NetLib.Objects;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace GUNRPG.Infrastructure.Identity;

/// <summary>
/// WebAuthn registration and authentication service backed by Fido2NetLib.
/// Handles challenge generation, credential verification, signature counter tracking,
/// and replay protection.
///
/// Origin validation is performed during startup via options validation.
/// </summary>
public sealed class WebAuthnService : IWebAuthnService
{
    private readonly IFido2 _fido2;
    private readonly Fido2Configuration _fido2Config;
    private readonly LiteDbWebAuthnStore _store;
    private readonly UserManager<ApplicationUser> _userManager;

    public WebAuthnService(
        IFido2 fido2,
        IOptions<Fido2Configuration> fido2Config,
        LiteDbWebAuthnStore store,
        UserManager<ApplicationUser> userManager)
    {
        _fido2 = fido2;
        _fido2Config = fido2Config.Value;
        _store = store;
        _userManager = userManager;
    }

    // ── Registration ─────────────────────────────────────────────────────────

    public async Task<ServiceResult<string>> BeginRegistrationAsync(string username, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(username))
            return Err(WebAuthnErrorCode.InvalidRequest, "Username is required.");

        var user = await _userManager.FindByNameAsync(username);
        if (user is null)
        {
            // Create the user on first WebAuthn registration
            user = new ApplicationUser { UserName = username };
            var result = await _userManager.CreateAsync(user);
            if (!result.Succeeded)
                return Err(WebAuthnErrorCode.InternalError,
                    string.Join("; ", result.Errors.Select(e => e.Description)));
        }

        var existingCredentials = _store.GetCredentialsByUserId(user.Id)
            .Select(c => new PublicKeyCredentialDescriptor(Base64UrlDecode(c.Id)))
            .ToList();

        var fido2User = new Fido2User
        {
            Id = Encoding.UTF8.GetBytes(user.Id),
            Name = username,
            DisplayName = username,
        };

        var options = _fido2.RequestNewCredential(new RequestNewCredentialParams
        {
            User = fido2User,
            ExcludeCredentials = existingCredentials,
            AuthenticatorSelection = AuthenticatorSelection.Default,
            AttestationPreference = AttestationConveyancePreference.None,
        });

        _store.StoreChallenge(username, options.Challenge);
        return ServiceResult<string>.Success(options.ToJson());
    }

    public async Task<ServiceResult<string>> CompleteRegistrationAsync(
        string username,
        string attestationResponseJson,
        CancellationToken ct = default)
    {
        var user = await _userManager.FindByNameAsync(username);
        if (user is null)
            return Err(WebAuthnErrorCode.UserNotFound, $"User '{username}' not found.");

        var challenge = _store.ConsumeChallenge(username);
        if (challenge is null)
            return Err(WebAuthnErrorCode.ChallengeMissing,
                "No pending registration challenge. Restart registration.");

        AuthenticatorAttestationRawResponse rawResponse;
        try
        {
            rawResponse = JsonSerializer.Deserialize<AuthenticatorAttestationRawResponse>(attestationResponseJson)
                ?? throw new JsonException("Null response");
        }
        catch (JsonException ex)
        {
            return Err(WebAuthnErrorCode.InvalidRequest, $"Malformed attestation response: {ex.Message}");
        }

        var options = CredentialCreateOptions.Create(
            _fido2Config,
            challenge,
            new Fido2User
            {
                Id = Encoding.UTF8.GetBytes(user.Id),
                Name = username,
                DisplayName = username,
            },
            AuthenticatorSelection.Default,
            AttestationConveyancePreference.None,
            excludeCredentials: [],
            extensions: null,
            pubKeyCredParams: PubKeyCredParam.Defaults);

        try
        {
            var credential = await _fido2.MakeNewCredentialAsync(
                new MakeNewCredentialParams
                {
                    AttestationResponse = rawResponse,
                    OriginalOptions = options,
                    IsCredentialIdUniqueToUserCallback = IsCredentialIdUniqueToUserAsync,
                }, ct);

            var storedCredential = new WebAuthnCredential
            {
                Id = Base64UrlEncode(credential.Id),
                UserId = user.Id,
                PublicKey = credential.PublicKey,
                SignatureCounter = credential.SignCount,
                AaGuid = credential.AaGuid,
                Transports = credential.Transports?.Select(t => t.ToString()).ToList() ?? [],
                RegisteredAt = DateTimeOffset.UtcNow,
            };

            _store.UpsertCredential(storedCredential);
            return ServiceResult<string>.Success(user.Id);
        }
        catch (Fido2VerificationException ex)
        {
            return Err(WebAuthnErrorCode.AttestationFailed, ex.Message);
        }
    }

    // ── Authentication ───────────────────────────────────────────────────────

    public async Task<ServiceResult<string>> BeginLoginAsync(string username, CancellationToken ct = default)
    {
        var user = await _userManager.FindByNameAsync(username);
        if (user is null)
            return Err(WebAuthnErrorCode.UserNotFound, $"User '{username}' not found.");

        var credentials = _store.GetCredentialsByUserId(user.Id)
            .Select(c => new PublicKeyCredentialDescriptor(Base64UrlDecode(c.Id)))
            .ToList();

        if (credentials.Count == 0)
            return Err(WebAuthnErrorCode.CredentialNotFound,
                $"No WebAuthn credentials registered for '{username}'.");

        var options = _fido2.GetAssertionOptions(new GetAssertionOptionsParams
        {
            AllowedCredentials = credentials,
            UserVerification = UserVerificationRequirement.Preferred,
        });

        _store.StoreChallenge(username, options.Challenge);
        return ServiceResult<string>.Success(options.ToJson());
    }

    public async Task<ServiceResult<string>> CompleteLoginAsync(
        string username,
        string assertionResponseJson,
        CancellationToken ct = default)
    {
        var user = await _userManager.FindByNameAsync(username);
        if (user is null)
            return Err(WebAuthnErrorCode.UserNotFound, $"User '{username}' not found.");

        var challenge = _store.ConsumeChallenge(username);
        if (challenge is null)
            return Err(WebAuthnErrorCode.ChallengeMissing,
                "No pending authentication challenge. Restart login.");

        AuthenticatorAssertionRawResponse rawResponse;
        try
        {
            rawResponse = JsonSerializer.Deserialize<AuthenticatorAssertionRawResponse>(assertionResponseJson)
                ?? throw new JsonException("Null response");
        }
        catch (JsonException ex)
        {
            return Err(WebAuthnErrorCode.InvalidRequest, $"Malformed assertion response: {ex.Message}");
        }

        var credentialId = Base64UrlEncode(rawResponse.RawId);
        var storedCredential = _store.GetCredentialById(credentialId);
        if (storedCredential is null || storedCredential.UserId != user.Id)
            return Err(WebAuthnErrorCode.CredentialNotFound,
                "Credential not registered or belongs to a different user.");

        var assertionOptions = AssertionOptions.Create(
            _fido2Config,
            challenge,
            allowedCredentials: [],
            userVerification: UserVerificationRequirement.Preferred,
            extensions: null);

        try
        {
            var result = await _fido2.MakeAssertionAsync(
                new MakeAssertionParams
                {
                    AssertionResponse = rawResponse,
                    OriginalOptions = assertionOptions,
                    StoredPublicKey = storedCredential.PublicKey,
                    StoredSignatureCounter = storedCredential.SignatureCounter,
                    IsUserHandleOwnerOfCredentialIdCallback = IsUserHandleOwnerOfCredentialIdAsync,
                }, ct);

            // Verify signature counter increased (replay / authenticator clone protection)
            if (storedCredential.SignatureCounter > 0 && result.SignCount <= storedCredential.SignatureCounter)
                return Err(WebAuthnErrorCode.CounterRegression,
                    $"Signature counter did not increase (stored={storedCredential.SignatureCounter}, received={result.SignCount}). " +
                    "The authenticator may be cloned.");

            // Update signature counter and last-used timestamp
            storedCredential.SignatureCounter = result.SignCount;
            storedCredential.LastUsedAt = DateTimeOffset.UtcNow;
            _store.UpsertCredential(storedCredential);

            return ServiceResult<string>.Success(user.Id);
        }
        catch (Fido2VerificationException ex)
        {
            return Err(WebAuthnErrorCode.AssertionFailed, ex.Message);
        }
    }

    // ── Delegates ─────────────────────────────────────────────────────────────

    private Task<bool> IsCredentialIdUniqueToUserAsync(IsCredentialIdUniqueToUserParams args, CancellationToken ct)
    {
        var credId = Base64UrlEncode(args.CredentialId.ToArray());
        var existing = _store.GetCredentialById(credId);
        return Task.FromResult(existing is null);
    }

    private Task<bool> IsUserHandleOwnerOfCredentialIdAsync(IsUserHandleOwnerOfCredentialIdParams args, CancellationToken ct)
    {
        var userId = Encoding.UTF8.GetString(args.UserHandle.ToArray());
        var credId = Base64UrlEncode(args.CredentialId.ToArray());
        var credential = _store.GetCredentialById(credId);
        return Task.FromResult(credential?.UserId == userId);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Builds a typed error result embedding the <see cref="WebAuthnErrorCode"/> in the message.</summary>
    private static ServiceResult<string> Err(WebAuthnErrorCode code, string message) =>
        ServiceResult<string>.InvalidState($"{code}: {message}");

    private static string Base64UrlEncode(byte[] data) =>
        Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Base64UrlDecode(string value)
    {
        value = value.Replace('-', '+').Replace('_', '/');
        switch (value.Length % 4)
        {
            case 2: value += "=="; break;
            case 3: value += "="; break;
        }
        return Convert.FromBase64String(value);
    }
}
