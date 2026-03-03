using GUNRPG.Application.Identity.Dtos;
using GUNRPG.Application.Results;

namespace GUNRPG.Application.Identity;

/// <summary>
/// Issues and validates JWT access tokens and manages refresh token rotation.
/// </summary>
public interface ITokenService
{
    /// <summary>
    /// Issues a new JWT access token and a fresh refresh token for the given user.
    /// The previous refresh token (if any) is consumed atomically.
    /// </summary>
    Task<TokenResponse> IssueTokensAsync(string userId, string? username, Guid? accountId, CancellationToken ct = default);

    /// <summary>
    /// Exchanges an existing refresh token for a new token pair.
    /// The old refresh token is consumed; returns Unauthorized if invalid, expired, or already used.
    /// </summary>
    Task<ServiceResult<TokenResponse>> RefreshAsync(string refreshToken, CancellationToken ct = default);

    /// <summary>
    /// Revokes all active refresh tokens for the given user (logout from all devices).
    /// </summary>
    Task RevokeAllAsync(string userId, CancellationToken ct = default);
}
