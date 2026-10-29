using System.Globalization;
using RoyConvert.Core.Ffmpeg;

namespace RoyConvert.Core;

/// <summary>Cuts a part out of a video or sound file into a new file next to it.</summary>
public static class MediaTrim
{
    public static readonly IReadOnlySet<string> Inputs =
        new HashSet<string>(Formats.VideoInputs.Concat(Formats.AudioInputs), StringComparer.OrdinalIgnoreCase);

    /// <summary>In the input's own format, except a precise cut of a video royconvert does not write (MOV → MP4).</summary>
    public static string OutputPathFor(string input, string suffix, bool precise, Func<string, bool>? exists = null)
    {
        var extension = precise && FfmpegArguments.TrimFormat(input) is { } format
            ? Formats.Extension(format)
            : Formats.ExtensionOf(input);
        return OutputPath.Beside(input, suffix, extension, exists);
    }

    /// <summary>
    /// Reads "75", "1:15", "0:01:15" or "1:02:03.5" (a comma works as the decimal mark too); null if it is not a time.
    /// </summary>
    public static TimeSpan? ParseTime(string text)
    {
        var parts = text.Trim().Replace(',', '.').Split(':');
        if (parts.Length is < 1 or > 3 || parts.Any(p => p.Length == 0))
        {
            return null;
        }

        double total = 0;
        for (var i = 0; i < parts.Length; i++)
        {
            var last = i == parts.Length - 1;
            // Only the seconds may have a fraction; minutes and seconds after the first part stay under 60.
            if (!double.TryParse(parts[i], last ? NumberStyles.AllowDecimalPoint : NumberStyles.None, CultureInfo.InvariantCulture, out var value)
                || (i > 0 && value >= 60))
            {
                return null;
            }

            total = total * 60 + value;
        }

        return TimeSpan.FromSeconds(total);
    }

    /// <summary>"1:02:03", "2:03" or "2:03.5": the way <see cref="ParseTime"/> reads it back.</summary>
    public static string FormatTime(TimeSpan time)
    {
        var text = time.TotalHours >= 1
            ? $"{(int)time.TotalHours}:{time.Minutes:00}:{time.Seconds:00}"
            : $"{time.Minutes}:{time.Seconds:00}";
        return time.Milliseconds == 0 ? text : $"{text}.{time.Milliseconds:000}".TrimEnd('0');
    }

    /// <param name="end">Null keeps everything after <paramref name="start"/>.</param>
    /// <exception cref="ConversionException">ffmpeg failed, or the times are outside the file.</exception>
    public static async Task TrimAsync(FfmpegConverter ffmpeg, string input, string output, TimeSpan start, TimeSpan? end,
        bool precise, ConversionSettings settings, IProgress<double?> progress, CancellationToken cancellationToken = default)
    {
        var length = await ffmpeg.ProbeDurationAsync(input, cancellationToken);
        if (end <= start || (length is { } l && start >= l))
        {
            throw new ConversionException($"The part {FormatTime(start)} – {(end is { } e ? FormatTime(e) : "end")} is not in the file"
                + (length is { } known ? $" ({FormatTime(known)} long)." : "."));
        }

        var until = end is { } e2 && length is { } l2 && e2 > l2 ? l2 : end ?? length;
        await ffmpeg.RunAsync(FfmpegArguments.BuildTrim(input, output, start, end, precise, settings), output,
            until - start, progress, cancellationToken);
    }
}
