namespace GUNRPG.Infrastructure;

/// <summary>
/// Utility helpers for file-system path handling.
/// </summary>
public static class PathHelpers
{
    /// <summary>
    /// Returns the current user's home directory or an empty string if unavailable.
    /// </summary>
    public static string GetUserHomeDirectory()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return string.IsNullOrWhiteSpace(home) ? string.Empty : home;
    }

    /// <summary>
    /// Expands a leading tilde to the user's home directory. Supports both ~/ and ~\ prefixes.
    /// </summary>
    public static string ExpandHomePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return path;

        var home = GetUserHomeDirectory();

        if (path == "~")
            return string.IsNullOrEmpty(home) ? path : home;

        if (path.StartsWith("~/", StringComparison.Ordinal) ||
            path.StartsWith("~\\", StringComparison.Ordinal))
        {
            var remainder = path[2..];
            return string.IsNullOrEmpty(home)
                ? remainder
                : Path.Combine(home, remainder);
        }

        return path;
    }
}
