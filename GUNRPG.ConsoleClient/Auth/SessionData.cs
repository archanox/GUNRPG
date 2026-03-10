namespace GUNRPG.ConsoleClient.Auth;

/// <summary>
/// Represents a persisted user session stored in <c>~/.gunrpg/session.json</c>.
/// Only the refresh token and minimal identity data are stored on disk;
/// the access token is kept in memory only.
/// </summary>
public sealed record SessionData(
    string RefreshToken,
    string UserId,
    DateTimeOffset CreatedAt
);
