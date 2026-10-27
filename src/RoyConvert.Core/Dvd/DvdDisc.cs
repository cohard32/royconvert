using System.Text.RegularExpressions;

namespace RoyConvert.Core.Dvd;

/// <summary>
/// A DVD-Video disc or a copy of one on disk: a VIDEO_TS folder with title sets VTS_nn_0.VOB (menu) and
/// VTS_nn_1.VOB… (the video, split into ~1 GB parts). The main title is the title set with the most video.
/// Copy-protected (CSS) commercial discs are not decrypted.
/// </summary>
/// <param name="Name">Volume label of a disc, otherwise the folder name; names the output.</param>
/// <param name="MainTitle">The VOB parts of the main title, in playback order.</param>
public sealed partial record DvdDisc(string VideoTs, string Name, IReadOnlyList<string> MainTitle, bool IsOpticalDrive)
{
    private const string VideoTsFolder = "VIDEO_TS";

    /// <summary>
    /// The disc at <paramref name="path"/>: a drive root, a folder containing VIDEO_TS, or VIDEO_TS itself.
    /// Null when there is no DVD video there.
    /// </summary>
    public static DvdDisc? Open(string path)
    {
        string full;
        try
        {
            full = Path.GetFullPath(path);
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }

        if (!Directory.Exists(full))
        {
            return null;
        }

        var trimmed = Path.TrimEndingDirectorySeparator(full);
        var videoTs = string.Equals(Path.GetFileName(trimmed), VideoTsFolder, StringComparison.OrdinalIgnoreCase)
            ? trimmed
            : Path.Combine(full, VideoTsFolder);
        if (!Directory.Exists(videoTs))
        {
            return null;
        }

        var mainTitle = Directory.EnumerateFiles(videoTs, "*.VOB")
            .Select(file => (File: file, Match: VobName().Match(Path.GetFileName(file))))
            .Where(v => v.Match.Success && v.Match.Groups["part"].Value != "0")
            .GroupBy(v => v.Match.Groups["set"].Value)
            .Select(set => set.OrderBy(v => int.Parse(v.Match.Groups["part"].Value)).Select(v => v.File).ToList())
            .MaxBy(parts => parts.Sum(file => new FileInfo(file).Length));
        if (mainTitle is null)
        {
            return null;
        }

        var root = Path.GetDirectoryName(videoTs)!;
        var drive = DriveOf(root);
        return new DvdDisc(videoTs, NameOf(root, drive), mainTitle, drive?.DriveType == DriveType.CDRom);
    }

    /// <summary>ffmpeg input reading the parts as one stream (the parts are cut mid-stream).</summary>
    public string FfmpegInput => "concat:" + string.Join('|', MainTitle);

    /// <summary>
    /// A stand-in input path for <see cref="OutputPath.Build"/>: "{dir}" is the user's Videos folder for a disc in a
    /// drive (it cannot be written to), otherwise the folder holding the DVD folder; "{name}" is <see cref="Name"/>.
    /// </summary>
    public string NamingPath
    {
        get
        {
            var root = Path.GetDirectoryName(VideoTs)!;
            var directory = IsOpticalDrive || Path.GetDirectoryName(root) is not { } parent
                ? Environment.GetFolderPath(Environment.SpecialFolder.MyVideos)
                : parent;
            // The extension is dropped by OutputPath, so names with dots ("Tatil.2010") stay whole.
            return Path.Combine(directory, Name + ".dvd");
        }
    }

    private static DriveInfo? DriveOf(string path)
    {
        try
        {
            return Path.GetPathRoot(path) is { Length: > 0 } root ? new DriveInfo(root) : null;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static string NameOf(string root, DriveInfo? drive)
    {
        var isDriveRoot = Path.GetDirectoryName(root) is null;
        if (isDriveRoot)
        {
            try
            {
                if (drive is { IsReady: true, VolumeLabel: { Length: > 0 } label })
                {
                    return label;
                }
            }
            catch (IOException)
            {
            }

            return "DVD";
        }

        return Path.GetFileName(root);
    }

    [GeneratedRegex(@"^VTS_(?<set>\d\d)_(?<part>\d)\.VOB$", RegexOptions.IgnoreCase)]
    private static partial Regex VobName();
}
