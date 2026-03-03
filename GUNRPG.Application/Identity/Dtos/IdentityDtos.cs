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
/// Response for polling the device code flow.
/// </summary>
public sealed record DevicePollResponse(
    string Status,       // "pending" | "authorized" | "expired"
    TokenResponse? Tokens
);
