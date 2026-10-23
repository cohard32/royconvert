using ImageMagick;

namespace RoyConvert.Core.Imaging;

public sealed class ImageConverter : IConverter
{
    /// <summary>Largest icon size supported by the ICO format.</summary>
    private const int IcoMaxSize = 256;

    public Task ConvertAsync(string input, string output, Preset preset, IProgress<double?> progress, CancellationToken cancellationToken) =>
        Task.Run(() => Convert(input, output, preset.Output, preset.Settings, progress, cancellationToken), cancellationToken);

    private static void Convert(string input, string output, OutputFormat format, ConversionSettings s, IProgress<double?> progress, CancellationToken cancellationToken)
    {
        progress.Report(null);
        try
        {
            // Only the first frame/page is converted; animated GIFs and multi-page TIFFs become a still image.
            using var image = new MagickImage(input);
            cancellationToken.ThrowIfCancellationRequested();

            image.AutoOrient();
            if (s.Rotation != Rotation.None)
            {
                image.Rotate((int)s.Rotation);
            }

            if (s.Scale.Height is { } height)
            {
                // Greater: only shrink images taller than the target, never upscale.
                image.Resize(new MagickGeometry(0, (uint)height) { Greater = true });
            }
            else if (s.Scale.Factor is { } factor && factor != 1.0)
            {
                image.Resize(new Percentage(factor * 100));
            }

            var maxSize = format == OutputFormat.Ico
                ? Math.Min(s.ImageMaxSize ?? IcoMaxSize, IcoMaxSize)
                : s.ImageMaxSize;
            if (maxSize is { } max && (image.Width > max || image.Height > max))
            {
                image.Resize(new MagickGeometry((uint)max, (uint)max) { Greater = true });
            }

            cancellationToken.ThrowIfCancellationRequested();

            image.Quality = (uint)Math.Clamp(s.ImageQuality, 1, 100);
            if (format == OutputFormat.Jpg)
            {
                // JPEG has no alpha channel; flatten onto white instead of black.
                image.BackgroundColor = MagickColors.White;
                image.Alpha(AlphaAction.Remove);
            }

            image.Write(output, ToMagickFormat(format));
        }
        catch (MagickException e)
        {
            throw new ConversionException(e.Message, e);
        }

        progress.Report(1);
    }

    private static MagickFormat ToMagickFormat(OutputFormat format) => format switch
    {
        OutputFormat.Png => MagickFormat.Png,
        OutputFormat.Jpg => MagickFormat.Jpeg,
        OutputFormat.Webp => MagickFormat.WebP,
        OutputFormat.Avif => MagickFormat.Avif,
        OutputFormat.Ico => MagickFormat.Ico,
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Not an image format."),
    };
}
