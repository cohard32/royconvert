using System.Globalization;
using System.Text;

namespace RoyConvert.Core.Ffmpeg;

/// <summary>Builds the ffmpeg argument list for a preset. Arguments are returned unquoted, for ProcessStartInfo.ArgumentList.</summary>
public static class FfmpegArguments
{
    public static IReadOnlyList<string> Build(string input, string output, OutputFormat format, ConversionSettings s) =>
        Build(input, output, format, s, dvd: false);

    /// <summary>
    /// The main title of a DVD as MP4 or MKV: the VOB parts are read as one stream, interlaced frames are deinterlaced
    /// and every audio track (language) is kept; DVD subtitles are left out.
    /// </summary>
    public static IReadOnlyList<string> BuildDvd(Dvd.DvdDisc disc, string output, OutputFormat format, ConversionSettings s)
    {
        if (format is not (OutputFormat.Mp4 or OutputFormat.Mkv))
        {
            throw new ArgumentOutOfRangeException(nameof(format), format, "DVDs are converted to MP4 or MKV.");
        }

        return Build(disc.FfmpegInput, output, format, s, dvd: true);
    }

    private static IReadOnlyList<string> Build(string input, string output, OutputFormat format, ConversionSettings s, bool dvd)
    {
        if (Formats.KindOf(format) == MediaKind.Image)
        {
            throw new ArgumentException($"{format} is an image format and is not produced by ffmpeg.", nameof(format));
        }

        var args = new List<string> { "-hide_banner", "-nostdin", "-n", "-progress", "pipe:1", "-nostats" };
        if (dvd)
        {
            // Audio streams of a VOB can start late; VOB timestamps restart at cell boundaries.
            args.AddRange(["-analyzeduration", "100M", "-probesize", "100M", "-fflags", "+genpts"]);
        }

        args.AddRange(["-i", input]);
        if (dvd)
        {
            args.AddRange(["-map", "0:v:0", "-map", "0:a?"]);
        }
        else if (CanCopyVideo(input, format, s))
        {
            // Only the sound goes: the picture is copied as it is, which is fast and loses nothing.
            args.AddRange(["-map", "0:v", "-c:v", "copy", "-an"]);
            if (format == OutputFormat.Mp4)
            {
                args.AddRange(["-movflags", "+faststart"]);
            }

            args.Add(output);
            return args;
        }

        switch (format)
        {
            case OutputFormat.Mp4:
            case OutputFormat.Mkv:
                AddVideoFilters(args, s, dvd ? DeinterlaceFilter : null);
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

    /// <summary>
    /// A video without sound in the container it already has (or in MKV, which holds any codec) needs no encoding:
    /// nothing else changes. Custom arguments may ask for more, so they always encode.
    /// </summary>
    public static bool CanCopyVideo(string input, OutputFormat format, ConversionSettings s) =>
        !s.EnableAudio && VideoFilter(s) is null && string.IsNullOrWhiteSpace(s.CustomFfmpegArguments)
        && Formats.VideoInputs.Contains(Formats.ExtensionOf(input))
        && (format == OutputFormat.Mkv || (Formats.KindOf(format) == MediaKind.Video && format != OutputFormat.Gif
            && Formats.ExtensionOf(input) == Formats.Extension(format)));

    /// <summary>
    /// Keeps <paramref name="start"/> to <paramref name="end"/> (null: to the end) of a video or sound file in its own
    /// format. Without <paramref name="precise"/> the streams are copied: fast and lossless, but a video starts at the
    /// key frame before <paramref name="start"/>. With it the video is encoded again (see <see cref="TrimFormat"/>)
    /// so the cut is frame exact.
    /// </summary>
    public static IReadOnlyList<string> BuildTrim(string input, string output, TimeSpan start, TimeSpan? end, bool precise,
        ConversionSettings s)
    {
        var args = new List<string> { "-hide_banner", "-nostdin", "-n", "-progress", "pipe:1", "-nostats" };
        // Before -i: seeks quickly; when encoding, ffmpeg still decodes from the key frame and drops what comes before.
        if (start > TimeSpan.Zero)
        {
            args.AddRange(["-ss", Seconds(start)]);
        }

        args.AddRange(["-i", input]);
        if (end is { } until)
        {
            args.AddRange(["-t", Seconds(until - start)]);
        }

        if (precise && TrimFormat(input) is { } format)
        {
            var encode = Build(input, output, format, s, dvd: false);
            // Everything after "-i <input>" of the normal conversion: codecs, quality, output.
            var afterInput = encode.ToList().IndexOf(input) + 1;
            args.AddRange(encode.Skip(afterInput));
            return args;
        }

        args.AddRange(["-map", "0:v?", "-map", "0:a?", "-c", "copy", "-avoid_negative_ts", "make_zero"]);
        args.Add(output);
        return args;
    }

    /// <summary>
    /// What a precise trim encodes to: the input's own format for the videos royconvert writes, MP4 for other videos,
    /// null for sound, which is always copied (its packets are a few milliseconds long).
    /// </summary>
    public static OutputFormat? TrimFormat(string input) => Formats.ExtensionOf(input) switch
    {
        "mkv" => OutputFormat.Mkv,
        "webm" => OutputFormat.Webm,
        "avi" => OutputFormat.Avi,
        var e when Formats.VideoInputs.Contains(e) => OutputFormat.Mp4,
        _ => null,
    };

    private static string Seconds(TimeSpan time) => time.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture);

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

    /// <summary>
    /// Deinterlaces only frames flagged as interlaced, so progressive DVDs pass unchanged. One frame per frame keeps
    /// the source rate (25 fps for PAL) instead of doubling it.
    /// </summary>
    private const string DeinterlaceFilter = "bwdif=mode=send_frame:deint=interlaced";

    private static void AddVideoFilters(List<string> args, ConversionSettings s, string? first = null)
    {
        var filter = string.Join(",", new[] { first, VideoFilter(s) }.OfType<string>());
        if (filter.Length > 0)
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
