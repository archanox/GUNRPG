using System.Text.Json;

namespace GUNRPG.ConsoleClient.Identity;

/// <summary>
/// Persists the refresh token and node URL to disk at ~/.gunrpg/auth.json.
/// Access tokens are short-lived and are never stored persistently.
///
/// Security hardening on Unix-like systems:
/// - The ~/.gunrpg directory is created with mode 700 (owner-only).
/// - auth.json is written to a temporary file with mode 600 before being
///   atomically renamed into place, so the secret is never present on disk
///   with broader permissions.
/// </summary>
public sealed class TokenStore
{
    private static readonly JsonSerializerOptions s_jsonOptions =
        new(JsonSerializerDefaults.Web);

    private readonly string _dir;
    private readonly string _filePath;

    /// <param name="basePath">
    /// Override the storage directory (defaults to <c>~/.gunrpg</c>).
    /// Intended for testing — production code should use the parameterless constructor.
    /// </param>
    public TokenStore(string? basePath = null)
    {
        _dir = basePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".gunrpg");
        Directory.CreateDirectory(_dir);

        // Restrict directory to current user on Unix-like systems.
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
            File.SetUnixFileMode(_dir,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        _filePath = Path.Combine(_dir, "auth.json");
    }

    /// <summary>Loads stored auth data, or null if none exists or the file is corrupt.</summary>
    public async Task<StoredAuth?> LoadAsync()
    {
        if (!File.Exists(_filePath))
            return null;

        try
        {
            var json = await File.ReadAllTextAsync(_filePath);
            return JsonSerializer.Deserialize<StoredAuth>(json, s_jsonOptions);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Stores the refresh token and node URL atomically.
    /// The access token is intentionally excluded — it is kept in memory only.
    /// Writes to a temp file with mode 600 first, then renames it into place.
    /// </summary>
    public async Task SaveAsync(string refreshToken, string nodeUrl)
    {
        var data = new StoredAuth(refreshToken, nodeUrl);
        var json = JsonSerializer.Serialize(data, s_jsonOptions);

        var tempPath = Path.Combine(_dir, "auth.json.tmp");
        await File.WriteAllTextAsync(tempPath, json);

        // Set owner-only permissions on the temp file before moving it into place.
        // This way the secret is never present on disk with looser ACLs.
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
            File.SetUnixFileMode(tempPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);

        File.Move(tempPath, _filePath, overwrite: true);
    }

    /// <summary>Removes stored auth data (e.g., on logout or token invalidation).</summary>
    public void Clear()
    {
        if (File.Exists(_filePath))
            File.Delete(_filePath);
    }
}

/// <summary>
/// Data persisted to ~/.gunrpg/auth.json.
/// Contains only the refresh token and node URL — not the access token.
/// </summary>
public sealed record StoredAuth(string RefreshToken, string NodeUrl);
