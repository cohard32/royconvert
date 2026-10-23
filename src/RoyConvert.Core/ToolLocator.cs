namespace RoyConvert.Core;

/// <summary>Finds bundled or system-installed command line tools.</summary>
public static class ToolLocator
{
    /// <summary>
    /// Looks for ffmpeg in: the ROYCONVERT_FFMPEG environment variable, the app's "tools" folder,
    /// the app folder, common Homebrew locations on macOS, then PATH.
    /// </summary>
    public static string? FindFfmpeg() => Find("ffmpeg", "ROYCONVERT_FFMPEG");

    public static string? Find(string tool, string? environmentVariable = null)
    {
        if (environmentVariable is not null && Environment.GetEnvironmentVariable(environmentVariable) is { Length: > 0 } fromEnv)
        {
            return File.Exists(fromEnv) ? fromEnv : null;
        }

        var fileName = OperatingSystem.IsWindows() ? tool + ".exe" : tool;

        var directories = new List<string>
        {
            Path.Combine(AppContext.BaseDirectory, "tools"),
            AppContext.BaseDirectory,
        };
        if (OperatingSystem.IsMacOS())
        {
            directories.AddRange(["/opt/homebrew/bin", "/usr/local/bin"]);
        }

        directories.AddRange((Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        return directories
            .Select(directory => Path.Combine(directory, fileName))
            .FirstOrDefault(File.Exists);
    }
}
