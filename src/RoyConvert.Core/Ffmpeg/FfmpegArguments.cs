using System.Globalization;
using System.Text;

namespace RoyConvert.Core.Ffmpeg;

/// <summary>Builds the ffmpeg argument list for a preset. Arguments are returned unquoted, for ProcessStartInfo.ArgumentList.</summary>
public static class FfmpegArguments
{
    public static IReadOnlyList<string> Build(string input, string output, OutputFormat format, ConversionSettings s)
    {
        if (Formats.KindOf(format) == MediaKind.Image)
        {
            throw new ArgumentException($"{format} is an image format and is not produced by ffmpeg.", nameof(format));
        }

        var args = new List<string> { "-hide_banner", "-nostdin", "-n", "-progress", "pipe:1", "-nostats", "-i", input };

        switch (format)
        {
            case OutputFormat.Mp4:
            case OutputFormat.Mkv:
                AddVideoFilters(args, s);
                args.AddRange(["-c:v", "libx264", "-preset", SpeedName(s.EncodingSpeed), "-crf", Num(Clamp(s.VideoQuality, 0, 51)), "-pix_fmt", "yuv420p"]);
                AddAudio(args, s, "aac", lossy: true);
                if (format == OutputFormat.Mp4)
                {
                    args.AddRange(["-movflags", "+faststart"]);
                }

                break;

            case OutputFormat.Webm:
                AddVideoFilters(args, s);
                // VP9's CRF scale is 0-63; offset so the same quality value gives a similar result to x264.
                args.AddRange(["-c:v", "libvpx-vp9", "-crf", Num(Clamp(s.VideoQuality + 8, 0, 63)), "-b:v", "0", "-row-mt", "1"]);
                AddAudio(args, s, "libopus", lossy: true);
                break;

            case OutputFormat.Avi:
                AddVideoFilters(args, s);
                // mpeg4 uses a 2-31 quantizer instead of CRF.
                args.AddRange(["-c:v", "mpeg4", "-vtag", "xvid", "-q:v", Num(Clamp((s.VideoQuality - 13) / 3, 2, 31))]);
                AddAudio(args, s, "libmp3lame", lossy: true);
                break;

            case OutputFormat.Gif:
                args.AddRange(["-an", "-vf", GifFilter(s), "-loop", "0"]);
                break;

            case OutputFormat.Mp3:
                args.Add("-vn");
                AddAudio(args, s with { EnableAudio = true }, "libmp3lame", lossy: true);
                break;

            case OutputFormat.Ogg:
                args.Add("-vn");
                AddAudio(args, s with { EnableAudio = true }, "libvorbis", lossy: true);
                break;

            case OutputFormat.Aac:
                args.Add("-vn");
                AddAudio(args, s with { EnableAudio = true }, "aac", lossy: true);
                break;

            case OutputFormat.Flac:
                args.Add("-vn");
                AddAudio(args, s with { EnableAudio = true }, "flac", lossy: false);
                break;

            case OutputFormat.Wav:
                args.Add("-vn");
                AddAudio(args, s with { EnableAudio = true }, "pcm_s16le", lossy: false);
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(format), format, null);
        }

        if (!string.IsNullOrWhiteSpace(s.CustomFfmpegArguments))
        {
            args.AddRange(SplitCommandLine(s.CustomFfmpegArguments));
        }

        args.Add(output);
        return args;
    }

    /// <summary>Video filter chain (rotation, then scale), or null when the video is copied as-is.</summary>
    public static string? VideoFilter(ConversionSettings s)
    {
        var filters = new List<string>();

        switch (s.Rotation)
        {
            case Rotation.Right:
                filters.Add("transpose=1");
                break;
            case Rotation.Left:
                filters.Add("transpose=2");
                break;
            case Rotation.UpsideDown:
                filters.Add("hflip,vflip");
                break;
        }

        // Encoders such as libx264 with yuv420p require even dimensions.
        if (s.Scale.Height is { } height)
        {
            // Only shrink: a 360p source must not be upscaled by a "720p" preset.
            filters.Add($"scale=-2:'min({Num(height - height % 2)},trunc(ih/2)*2)'");
        }
        else if (s.Scale.Factor is { } factor && factor != 1.0)
        {
            var f = factor.ToString(CultureInfo.InvariantCulture);
            filters.Add($"scale=trunc(iw*{f}/2)*2:trunc(ih*{f}/2)*2");
        }

        return filters.Count == 0 ? null : string.Join(",", filters);
    }

    public static string SpeedName(EncodingSpeed speed) => speed.ToString().ToLowerInvariant();

    /// <summary>Splits a user-supplied argument string, honoring double quotes.</summary>
    public static IEnumerable<string> SplitCommandLine(string text)
    {
        var current = new StringBuilder();
        var inQuotes = false;
        var hasToken = false;

        foreach (var c in text)
        {
            if (c == '"')
            {
                inQuotes = !inQuotes;
                hasToken = true;
            }
            else if (char.IsWhiteSpace(c) && !inQuotes)
            {
                if (hasToken)
                {
                    yield return current.ToString();
                    current.Clear();
                    hasToken = false;
                }
            }
            else
            {
                current.Append(c);
                hasToken = true;
            }
        }

        if (hasToken)
        {
            yield return current.ToString();
        }
    }

    private static void AddVideoFilters(List<string> args, ConversionSettings s)
    {
        if (VideoFilter(s) is { } filter)
        {
            args.AddRange(["-vf", filter]);
        }
    }

    private static void AddAudio(List<string> args, ConversionSettings s, string codec, bool lossy)
    {
        if (!s.EnableAudio)
        {
            args.Add("-an");
            return;
        }

        args.AddRange(["-c:a", codec]);
        if (lossy)
        {
            args.AddRange(["-b:a", $"{Num(s.AudioBitrate)}k"]);
        }

        if (s.AudioChannels is { } channels)
        {
            args.AddRange(["-ac", Num(channels)]);
        }
    }

    private static string GifFilter(ConversionSettings s)
    {
        var prefix = VideoFilter(s) is { } filter ? filter + "," : string.Empty;
        // Two-pass palette in a single graph: much better colors than ffmpeg's default GIF palette.
        return $"{prefix}fps={Num(s.GifFramesPerSecond)},split[a][b];[a]palettegen=stats_mode=diff[p];[b][p]paletteuse=dither=bayer:bayer_scale=5";
    }

    private static int Clamp(int value, int min, int max) => Math.Clamp(value, min, max);

    private static string Num(int value) => value.ToString(CultureInfo.InvariantCulture);
}
