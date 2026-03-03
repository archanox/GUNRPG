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
    /// </summary>
    Task<ServiceResult<string>> CompleteRegistrationAsync(
        string username,
        string attestationResponseJson,
        CancellationToken ct = default);

    /// <summary>
    /// Begins a WebAuthn authentication assertion.
    /// Returns a JSON options object to send to the browser's navigator.credentials.get().
    /// </summary>
    Task<ServiceResult<string>> BeginLoginAsync(string username, CancellationToken ct = default);

    /// <summary>
    /// Completes WebAuthn authentication, updates the signature counter, and returns the user ID.
    /// </summary>
    Task<ServiceResult<string>> CompleteLoginAsync(
        string username,
        string assertionResponseJson,
        CancellationToken ct = default);
}
