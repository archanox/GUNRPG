using System.Text.Json;

namespace GUNRPG.ConsoleClient.Identity;

/// <summary>
/// Persists the refresh token and node URL to disk at ~/.gunrpg/auth.json.
/// Access tokens are short-lived and are never stored persistently.
/// The file is created with owner-only read/write permissions (600) on Unix-like systems.
/// </summary>
public sealed class TokenStore
{
    private static readonly JsonSerializerOptions s_jsonOptions =
        new(JsonSerializerDefaults.Web);

    private readonly string _filePath;

    public TokenStore()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".gunrpg");
        Directory.CreateDirectory(dir);
        _filePath = Path.Combine(dir, "auth.json");
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
    /// Stores the refresh token and node URL.
    /// The access token is intentionally excluded — it is kept in memory only.
    /// Sets file permissions to owner-only (600) on Unix-like systems.
    /// </summary>
    public async Task SaveAsync(string refreshToken, string nodeUrl)
    {
        var data = new StoredAuth(refreshToken, nodeUrl);
        var json = JsonSerializer.Serialize(data, s_jsonOptions);
        await File.WriteAllTextAsync(_filePath, json);

        // Restrict to owner read/write only on Unix-like systems.
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
            File.SetUnixFileMode(_filePath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
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
