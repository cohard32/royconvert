namespace RoyConvert.Core;

/// <summary>
/// Builds output paths from a template. Tokens:
/// <c>{dir}</c> folder of the input file, <c>{name}</c> file name without extension,
/// <c>{ext}</c> original extension. The output extension is always appended.
/// </summary>
public static class OutputPath
{
    public const string DefaultTemplate = "{dir}/{name}";

    public static string Build(string inputPath, string template, OutputFormat format, Func<string, bool>? exists = null)
    {
        exists ??= File.Exists;

        var full = Path.GetFullPath(inputPath);
        var stem = template
            .Replace("{dir}", Path.GetDirectoryName(full) ?? string.Empty)
            .Replace("{name}", Path.GetFileNameWithoutExtension(full))
            .Replace("{ext}", Formats.ExtensionOf(full));
        var extension = "." + Formats.Extension(format);

        var candidate = Path.GetFullPath(stem + extension);
        // Never overwrite: neither an existing file nor the input itself (e.g. mp4 -> mp4).
        for (var i = 1; exists(candidate) || PathsEqual(candidate, full); i++)
        {
            candidate = Path.GetFullPath($"{stem} ({i}){extension}");
        }

        return candidate;
    }

    /// <summary>"{dir}/{name}{suffix}.{extension}" next to the input, numbered " (1)", " (2)"… if it exists.</summary>
    public static string Beside(string inputPath, string suffix, string extension, Func<string, bool>? exists = null)
    {
        exists ??= Path.Exists;
        var full = Path.GetFullPath(inputPath);
        var stem = Path.Combine(Path.GetDirectoryName(full)!, Path.GetFileNameWithoutExtension(full) + suffix);
        var candidate = $"{stem}.{extension}";
        for (var i = 1; exists(candidate) || PathsEqual(candidate, full); i++)
        {
            candidate = $"{stem} ({i}).{extension}";
        }

        return candidate;
    }

    private static bool PathsEqual(string a, string b) =>
        string.Equals(a, b, OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal);
}
