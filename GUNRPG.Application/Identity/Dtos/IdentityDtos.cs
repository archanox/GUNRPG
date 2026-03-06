namespace GUNRPG.Application.Identity.Dtos;

/// <summary>
/// Pair of tokens issued after successful authentication or token refresh.
/// </summary>
public sealed record TokenResponse(
    string AccessToken,
    string RefreshToken,
    DateTimeOffset AccessTokenExpiresAt,
    DateTimeOffset RefreshTokenExpiresAt
);

/// <summary>
/// Response returned when a console client starts the device code flow.
/// Fields follow RFC 8628 §3.2 naming for interoperability.
/// </summary>
public sealed record DeviceCodeResponse(
    string DeviceCode,
    string UserCode,
    string VerificationUri,
    int ExpiresInSeconds,
    int PollIntervalSeconds
);

/// <summary>
/// Request body for refreshing a JWT access token.
/// </summary>
public sealed record RefreshTokenRequest(string RefreshToken);

/// <summary>
/// Request body for starting WebAuthn registration or authentication.
/// </summary>
public sealed record WebAuthnBeginRequest(string Username);

/// <summary>
/// Request body for completing WebAuthn registration.
/// </summary>
public sealed record WebAuthnRegisterCompleteRequest(
    string Username,
    string AttestationResponseJson
);

/// <summary>
/// Request body for completing WebAuthn authentication.
/// </summary>
public sealed record WebAuthnLoginCompleteRequest(
    string Username,
    string AssertionResponseJson
);

/// <summary>
/// Response body for beginning a usernameless (discoverable) WebAuthn login.
/// The session ID must be echoed back in the complete request.
/// </summary>
public sealed record WebAuthnDiscoverableLoginBeginResponse(string SessionId, string OptionsJson);

/// <summary>
/// Request body for completing a usernameless (discoverable) WebAuthn login.
/// </summary>
public sealed record WebAuthnDiscoverableLoginCompleteRequest(
    string SessionId,
    string AssertionResponseJson
);

/// <summary>
/// Request body for polling the device code flow.
/// </summary>
public sealed record DevicePollRequest(string DeviceCode);

/// <summary>
/// Response for polling the device code flow.
/// Status values align with RFC 8628 §3.5 error codes:
///   "authorization_pending" — user has not yet acted
///   "slow_down"             — client must back off (poll interval increased)
///   "expired_token"         — device code expired; restart the flow
///   "authorized"            — user authorized; tokens are present
/// </summary>
public sealed record DevicePollResponse(
    string Status,
    TokenResponse? Tokens
);

/// <summary>
/// Categories of WebAuthn errors returned to clients for better debugging.
/// </summary>
public enum WebAuthnErrorCode
{
    InvalidRequest,       // Missing or malformed request fields
    ChallengeMissing,     // No pending challenge found (session lost / timeout)
    AttestationFailed,    // Credential registration verification error
    AssertionFailed,      // Authentication assertion verification error
    CredentialNotFound,   // Credential not registered or belongs to another user
    CounterRegression,    // Signature counter did not increase (possible replay / clone)
    UserNotFound,
    InternalError,
}

/// <summary>
/// Structured WebAuthn error response for client debugging.
/// </summary>
public sealed record WebAuthnErrorResponse(
    WebAuthnErrorCode Code,
    string Message
);
