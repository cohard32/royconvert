namespace RoyConvert.Core;

public enum MediaKind
{
    Video,
    Audio,
    Image,
    Document,
}

public enum OutputFormat
{
    Mp4,
    Mkv,
    Webm,
    Avi,
    Gif,
    Mp3,
    Ogg,
    Flac,
    Wav,
    Aac,
    Png,
    Jpg,
    Webp,
    Avif,
    Ico,
    Pdf,
    Docx,
}

public static class Formats
{
    public static readonly IReadOnlySet<string> VideoInputs = Set(
        "3gp", "3gpp", "avi", "flv", "m4v", "mkv", "mov", "mp4", "mpeg", "mpg", "mts", "m2ts", "ogv", "ts", "vob", "webm", "wmv");

    public static readonly IReadOnlySet<string> AudioInputs = Set(
        "aac", "aiff", "ape", "flac", "m4a", "m4b", "mp3", "oga", "ogg", "opus", "wav", "wma");

    public static readonly IReadOnlySet<string> ImageInputs = Set(
        "avif", "bmp", "dng", "exr", "gif", "heic", "heif", "ico", "jfif", "jpeg", "jpg", "png", "psd", "svg", "tga", "tif", "tiff", "webp");

    /// <summary>Word processor, spreadsheet and presentation files that LibreOffice can export to PDF.</summary>
    public static readonly IReadOnlySet<string> OfficeInputs = Set(
        "doc", "docx", "odp", "ods", "odt", "ppt", "pptx", "rtf", "xls", "xlsx");

    public static bool IsPdf(string path) => ExtensionOf(path) == "pdf";

    public static string Extension(OutputFormat format) => format switch
    {
        OutputFormat.Jpg => "jpg",
        _ => format.ToString().ToLowerInvariant(),
    };

    public static MediaKind KindOf(OutputFormat format) => format switch
    {
        OutputFormat.Mp4 or OutputFormat.Mkv or OutputFormat.Webm or OutputFormat.Avi or OutputFormat.Gif => MediaKind.Video,
        OutputFormat.Mp3 or OutputFormat.Ogg or OutputFormat.Flac or OutputFormat.Wav or OutputFormat.Aac => MediaKind.Audio,
        OutputFormat.Pdf or OutputFormat.Docx => MediaKind.Document,
        _ => MediaKind.Image,
    };

    /// <summary>Normalized extension without the leading dot, e.g. "C:\a\b.MP4" -> "mp4".</summary>
    public static string ExtensionOf(string path) =>
        Path.GetExtension(path).TrimStart('.').ToLowerInvariant();

    private static IReadOnlySet<string> Set(params string[] items) =>
        new HashSet<string>(items, StringComparer.OrdinalIgnoreCase);
}
