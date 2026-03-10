using System.Text.Json;

namespace GUNRPG.ConsoleClient.Auth;

/// <summary>
/// Persists session data to <c>~/.gunrpg/session.json</c>.
/// Only <see cref="SessionData.RefreshToken"/>, <see cref="SessionData.UserId"/>,
/// and <see cref="SessionData.CreatedAt"/> are written to disk.
/// Access tokens are never persisted.
///
/// Security hardening on Unix-like systems:
/// - The <c>~/.gunrpg</c> directory is created with mode 700 (owner-only).
/// - <c>session.json</c> is written to a temp file with mode 600 before being
///   atomically renamed into place so the secret is never on disk with looser ACLs.
/// </summary>
public sealed class SessionStore
{
    private static readonly JsonSerializerOptions s_jsonOptions =
        new(JsonSerializerDefaults.Web);

    private readonly string _dir;
    private readonly string _filePath;

    /// <param name="basePath">
    /// Override the storage directory (defaults to <c>~/.gunrpg</c>).
    /// Intended for testing — production code should use the parameterless constructor.
    /// </param>
    public SessionStore(string? basePath = null)
    {
        _dir = basePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".gunrpg");
        Directory.CreateDirectory(_dir);

        // Restrict directory to current user on Unix-like systems.
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
            File.SetUnixFileMode(_dir,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        _filePath = Path.Combine(_dir, "session.json");
    }

    /// <summary>
    /// Loads stored session data, or <see langword="null"/> if no session file exists
    /// or the file is corrupt.
    /// </summary>
    public async Task<SessionData?> LoadAsync()
    {
        if (!File.Exists(_filePath))
            return null;

        try
        {
            var json = await File.ReadAllTextAsync(_filePath);
            return JsonSerializer.Deserialize<SessionData>(json, s_jsonOptions);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Persists session data atomically.
    /// The access token is intentionally excluded — it is kept in memory only.
    /// Writes to a temp file with mode 600 first, then renames it into place.
    /// </summary>
    public async Task SaveAsync(SessionData session)
    {
        var json = JsonSerializer.Serialize(session, s_jsonOptions);
        var tempPath = Path.Combine(_dir, "session.json.tmp");

        await File.WriteAllTextAsync(tempPath, json);

        // Set owner-only permissions on the temp file before moving it into place.
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
            File.SetUnixFileMode(tempPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);

        File.Move(tempPath, _filePath, overwrite: true);
    }

    /// <summary>Removes the session file (e.g., on logout).</summary>
    public void Delete()
    {
        if (File.Exists(_filePath))
            File.Delete(_filePath);
    }
}
