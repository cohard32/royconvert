using System.Globalization;
using System.Text.RegularExpressions;

namespace RoyConvert.Core.Ffmpeg;

/// <summary>Parses ffmpeg's stderr header and its "-progress pipe:1" key=value stream.</summary>
public static partial class FfmpegProgress
{
    /// <summary>Reads the input duration from a stderr line such as "  Duration: 00:01:02.50, start: ...".</summary>
    public static TimeSpan? ParseDuration(string line)
    {
        var match = DurationRegex().Match(line);
        if (!match.Success)
        {
            return null;
        }

        var hours = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        var minutes = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
        var seconds = double.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture);
        return TimeSpan.FromHours(hours) + TimeSpan.FromMinutes(minutes) + TimeSpan.FromSeconds(seconds);
    }

    /// <summary>Reads the encoded position from a progress line ("out_time_us=..." or the identically scaled "out_time_ms=...").</summary>
    public static TimeSpan? ParseOutTime(string line)
    {
        var separator = line.IndexOf('=');
        if (separator < 0)
        {
            return null;
        }

        var key = line.AsSpan(0, separator);
        if (!key.SequenceEqual("out_time_us") && !key.SequenceEqual("out_time_ms"))
        {
            return null;
        }

        // ffmpeg reports "N/A" before the first frame is written.
        return long.TryParse(line.AsSpan(separator + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out var micros) && micros >= 0
            ? TimeSpan.FromTicks(micros * 10)
            : null;
    }

    public static bool IsEnd(string line) => line == "progress=end";

    /// <summary>Fraction in [0, 1], or null when the duration is unknown.</summary>
    public static double? Fraction(TimeSpan position, TimeSpan? duration) =>
        duration is { Ticks: > 0 } d ? Math.Clamp(position / d, 0, 1) : null;

    [GeneratedRegex(@"Duration:\s*(\d+):(\d{2}):(\d{2}(?:\.\d+)?)")]
    private static partial Regex DurationRegex();
}
