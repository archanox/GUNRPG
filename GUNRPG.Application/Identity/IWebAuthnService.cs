using GUNRPG.Application.Identity.Dtos;
using GUNRPG.Application.Results;

namespace GUNRPG.Application.Identity;

/// <summary>
/// Manages WebAuthn credential registration and authentication.
/// Abstracts Fido2NetLib to keep the application layer free of library types.
/// </summary>
public interface IWebAuthnService
{
    /// <summary>
    /// Begins credential registration for a user.
    /// Returns a JSON options object to send to the browser's navigator.credentials.create().
    /// </summary>
    Task<ServiceResult<string>> BeginRegistrationAsync(string username, CancellationToken ct = default);

    /// <summary>
    /// Completes credential registration, persists the credential, and returns the user ID.
    /// Returns a typed <see cref="WebAuthnErrorCode"/> inside the error message for client debugging.
    /// Format: "ERROR_CODE: human readable message" when <see cref="ServiceResult{T}.IsSuccess"/> is false.
    /// </summary>
    Task<ServiceResult<string>> CompleteRegistrationAsync(
        string username,
        string attestationResponseJson,
        CancellationToken ct = default);

    /// <summary>
    /// Begins a WebAuthn authentication assertion for a known username.
    /// Returns a JSON options object to send to the browser's navigator.credentials.get().
    /// </summary>
    Task<ServiceResult<string>> BeginLoginAsync(string username, CancellationToken ct = default);

    /// <summary>
    /// Completes WebAuthn authentication, updates the signature counter, and returns the user ID.
    /// Returns a typed <see cref="WebAuthnErrorCode"/> inside the error message for client debugging.
    /// Format: "ERROR_CODE: human readable message" when <see cref="ServiceResult{T}.IsSuccess"/> is false.
    /// </summary>
    Task<ServiceResult<string>> CompleteLoginAsync(
        string username,
        string assertionResponseJson,
        CancellationToken ct = default);

    /// <summary>
    /// Begins a usernameless (discoverable credential) WebAuthn authentication assertion.
    /// Returns a session ID and a JSON options object with an empty allowCredentials list so the
    /// browser can discover resident credentials without the user entering a username first.
    /// </summary>
    Task<ServiceResult<(string SessionId, string OptionsJson)>> BeginDiscoverableLoginAsync(CancellationToken ct = default);

    /// <summary>
    /// Completes a usernameless WebAuthn authentication.  The user is identified from the
    /// credential ID in the assertion rather than from a supplied username.
    /// Returns the authenticated user ID on success.
    /// </summary>
    Task<ServiceResult<string>> CompleteDiscoverableLoginAsync(
        string sessionId,
        string assertionResponseJson,
        CancellationToken ct = default);
}
