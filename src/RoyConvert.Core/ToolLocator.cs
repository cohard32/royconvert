namespace RoyConvert.Core;

/// <summary>Finds bundled or system-installed command line tools.</summary>
public static class ToolLocator
{
    /// <summary>
    /// Looks for ffmpeg in: the ROYCONVERT_FFMPEG environment variable, the app's "tools" folder,
    /// the app folder, common Homebrew locations on macOS, then PATH.
    /// </summary>
    public static string? FindFfmpeg() => Find("ffmpeg", "ROYCONVERT_FFMPEG");

    /// <summary>
    /// Looks for LibreOffice's soffice in: the ROYCONVERT_SOFFICE environment variable, the copy bundled by the
    /// installer (tools/LibreOffice), the usual install folders (Program Files on Windows, /Applications on macOS),
    /// then the app folders and PATH like <see cref="Find"/>.
    /// </summary>
    public static string? FindLibreOffice()
    {
        if (Environment.GetEnvironmentVariable("ROYCONVERT_SOFFICE") is { Length: > 0 } fromEnv)
        {
            return File.Exists(fromEnv) ? fromEnv : null;
        }

        var installed = new List<string>();
        if (OperatingSystem.IsWindows())
        {
            installed.Add(Path.Combine(AppContext.BaseDirectory, "tools", "LibreOffice", "program", "soffice.exe"));
            foreach (var folder in new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86 })
            {
                installed.Add(Path.Combine(Environment.GetFolderPath(folder), "LibreOffice", "program", "soffice.exe"));
            }
        }
        else if (OperatingSystem.IsMacOS())
        {
            installed.Add("/Applications/LibreOffice.app/Contents/MacOS/soffice");
        }

        return installed.FirstOrDefault(File.Exists) ?? Find("soffice");
    }

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
