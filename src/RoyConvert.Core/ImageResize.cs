using System.Globalization;
using ImageMagick;

namespace RoyConvert.Core;

/// <summary>
/// What a picture is made to fit: a width and/or height in pixels (the proportions are kept; with both, the picture
/// fits inside that box) or a percentage of its own size.
/// </summary>
public sealed record ResizeOptions(int? Width = null, int? Height = null, double? Percent = null);

/// <summary>
/// Makes pictures smaller into a new file next to each one. Never enlarges: a picture already within the size is
/// written as it is (still useful for HEIC or camera raw, which become JPEG).
/// </summary>
public static class ImageResize
{
    /// <summary>Largest width or height that can be typed.</summary>
    public const int MaxPixels = 100_000;

    /// <summary>Pictures that can be resized: not icons or vector drawings, which have no single pixel size.</summary>
    public static readonly IReadOnlySet<string> Inputs = new HashSet<string>(
        Formats.ImageInputs.Except(["ico", "svg", "exr"]), StringComparer.OrdinalIgnoreCase);

    /// <summary>Kept as they are; other pictures (camera raw, HEIC, PSD, TGA) are written as JPEG.</summary>
    private static readonly IReadOnlySet<string> KeptFormats = new HashSet<string>(
        ["avif", "bmp", "gif", "jfif", "jpeg", "jpg", "png", "tif", "tiff", "webp"], StringComparer.OrdinalIgnoreCase);

    /// <summary>"{dir}/{name}{suffix}.{ext}", numbered if it exists; JPEG for pictures that are not kept as they are.</summary>
    public static string OutputPathFor(string input, string suffix, Func<string, bool>? exists = null)
    {
        var extension = Formats.ExtensionOf(input);
        return OutputPath.Beside(input, suffix, KeptFormats.Contains(extension) ? extension : "jpg", exists);
    }

    /// <summary>A whole number of pixels from 1 to <see cref="MaxPixels"/>; null if it is not one.</summary>
    public static int? ParsePixels(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.EndsWith("px", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed[..^2].TrimEnd();
        }

        return int.TryParse(trimmed, NumberStyles.None, CultureInfo.InvariantCulture, out var value) && value is >= 1 and <= MaxPixels
            ? value
            : null;
    }

    /// <summary>"50", "50%" or "12,5" (a comma works as the decimal mark): more than 0 and below 100; null otherwise.</summary>
    public static double? ParsePercent(string text)
    {
        var trimmed = text.Trim().TrimStart('%').TrimEnd('%').Trim().Replace(',', '.');
        return double.TryParse(trimmed, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value)
               && value is > 0 and < 100
            ? value
            : null;
    }

    /// <summary>The size a <paramref name="width"/> × <paramref name="height"/> picture gets; never larger than it is.</summary>
    public static (uint Width, uint Height) TargetSize(uint width, uint height, ResizeOptions options)
    {
        var scale = options switch
        {
            { Percent: { } percent } => percent / 100,
            { Width: { } w, Height: { } h } => Math.Min((double)w / width, (double)h / height),
            { Width: { } w } => (double)w / width,
            { Height: { } h } => (double)h / height,
            _ => 1,
        };
        if (scale >= 1)
        {
            return (width, height);
        }

        return ((uint)Math.Max(1, Math.Round(width * scale)), (uint)Math.Max(1, Math.Round(height * scale)));
    }

    /// <summary>The upright size of the first picture in the file, as <see cref="Apply"/> sees it.</summary>
    /// <exception cref="ConversionException">The file cannot be read.</exception>
    public static (uint Width, uint Height) ReadSize(string input)
    {
        try
        {
            // Read and turned the same way as Apply (Ping skips the orientation, and HEIC may come already upright).
            using var image = new MagickImage(input);
            image.AutoOrient();
            return (image.Width, image.Height);
        }
        catch (Exception e) when (e is MagickException or IOException or UnauthorizedAccessException)
        {
            throw new ConversionException(e.Message, e);
        }
    }

    /// <param name="quality">JPEG/WebP/AVIF quality for a picture written in another format than it was (e.g. HEIC → JPEG);
    /// one kept in its own format keeps its own quality.</param>
    /// <exception cref="ConversionException">The file cannot be read or written.</exception>
    public static void Apply(string input, string output, ResizeOptions options, int quality)
    {
        try
        {
            var sameFormat = string.Equals(Formats.ExtensionOf(input), Formats.ExtensionOf(output), StringComparison.OrdinalIgnoreCase);
            if (Formats.ExtensionOf(input) == "gif")
            {
                ResizeAnimation(input, output, options);
                return;
            }

            using var image = new MagickImage(input);
            image.AutoOrient();
            var (width, height) = TargetSize(image.Width, image.Height, options);
            if (width != image.Width || height != image.Height)
            {
                image.Resize(new MagickGeometry(width, height) { IgnoreAspectRatio = true });
            }

            if (!sameFormat || image.Quality == 0)
            {
                image.Quality = (uint)Math.Clamp(quality, 1, 100);
            }

            if (sameFormat)
            {
                image.Write(output, image.Format);
            }
            else
            {
                // JPEG has no alpha channel; flatten onto white instead of black.
                image.BackgroundColor = MagickColors.White;
                image.Alpha(AlphaAction.Remove);
                image.Write(output, MagickFormat.Jpeg);
            }
        }
        catch (Exception e) when (e is MagickException or IOException or UnauthorizedAccessException)
        {
            try
            {
                File.Delete(output);
            }
            catch (IOException)
            {
                // Nothing usable was written either way.
            }

            throw new ConversionException(e.Message, e);
        }
    }

    /// <summary>Every frame of an animated GIF, at the same size.</summary>
    private static void ResizeAnimation(string input, string output, ResizeOptions options)
    {
        using var frames = new MagickImageCollection(input);
        frames.Coalesce();
        var (width, height) = TargetSize(frames[0].Width, frames[0].Height, options);
        if (width != frames[0].Width || height != frames[0].Height)
        {
            foreach (var frame in frames)
            {
                frame.Resize(new MagickGeometry(width, height) { IgnoreAspectRatio = true });
            }
        }

        frames.Optimize();
        frames.Write(output, MagickFormat.Gif);
    }
}
