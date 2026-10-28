using Docnet.Core;
using Docnet.Core.Exceptions;
using Docnet.Core.Models;
using ImageMagick;

namespace RoyConvert.Core.Imaging;

/// <summary>
/// Converts images with Magick.NET, including image → PDF (one page). PDF inputs are rendered with PDFium,
/// one output file per page.
/// </summary>
public sealed class ImageConverter : IConverter
{
    /// <summary>Largest icon size supported by the ICO format.</summary>
    private const int IcoMaxSize = 256;

    /// <summary>PDF user space units per inch.</summary>
    private const double PdfPointsPerInch = 72;

    public Task ConvertAsync(string input, string output, Preset preset, IProgress<double?> progress, CancellationToken cancellationToken) =>
        Task.Run(() =>
        {
            if (Formats.IsPdf(input))
            {
                ConvertPdf(input, output, preset.Output, preset.Settings, progress, cancellationToken);
            }
            else
            {
                Convert(input, output, preset.Output, preset.Settings, progress, cancellationToken);
            }
        }, cancellationToken);

    /// <summary>Writes the images as the pages of one PDF, in the given order.</summary>
    public Task CombineAsync(IReadOnlyList<string> inputs, string output, Preset preset, IProgress<double?> progress, CancellationToken cancellationToken)
    {
        if (preset.Output != OutputFormat.Pdf)
        {
            throw new ArgumentOutOfRangeException(nameof(preset), preset.Output, "Images can only be combined into a PDF.");
        }

        return Task.Run(() => CombineToPdf(inputs, output, preset.Settings, progress, cancellationToken), cancellationToken);
    }

    /// <summary>
    /// Output paths for a document with <paramref name="pageCount"/> pages: the chosen output for a single page,
    /// otherwise "name-01.png", "name-02.png"… next to it. Existing files are never overwritten.
    /// </summary>
    public static IReadOnlyList<string> PagePaths(string output, int pageCount, Func<string, bool>? exists = null)
    {
        if (pageCount == 1)
        {
            return [output];
        }

        exists ??= File.Exists;
        var stem = Path.Combine(Path.GetDirectoryName(output) ?? string.Empty, Path.GetFileNameWithoutExtension(output));
        var extension = Path.GetExtension(output);
        var digits = Math.Max(2, pageCount.ToString().Length);

        var paths = new List<string>(pageCount);
        for (var page = 1; page <= pageCount; page++)
        {
            var pageStem = $"{stem}-{page.ToString().PadLeft(digits, '0')}";
            var candidate = pageStem + extension;
            for (var i = 1; exists(candidate); i++)
            {
                candidate = $"{pageStem} ({i}){extension}";
            }

            paths.Add(candidate);
        }

        return paths;
    }

    private static void Convert(string input, string output, OutputFormat format, ConversionSettings s, IProgress<double?> progress, CancellationToken cancellationToken)
    {
        progress.Report(null);
        try
        {
            // Only the first frame/page is converted; animated GIFs and multi-page TIFFs become a still image.
            using var image = new MagickImage(input);
            cancellationToken.ThrowIfCancellationRequested();
            Process(image, format, s);
            if (format == OutputFormat.Pdf)
            {
                PrepareForPdf(image, input);
            }

            cancellationToken.ThrowIfCancellationRequested();
            image.Write(output, ToMagickFormat(format));
        }
        catch (MagickException e)
        {
            throw new ConversionException(e.Message, e);
        }

        progress.Report(1);
    }

    private static void ConvertPdf(string input, string output, OutputFormat format, ConversionSettings s, IProgress<double?> progress, CancellationToken cancellationToken)
    {
        progress.Report(0);
        var written = new List<string>();
        try
        {
            using var document = DocLib.Instance.GetDocReader(input, new PageDimensions(s.PdfDpi / PdfPointsPerInch));
            var pageCount = document.GetPageCount();
            if (pageCount == 0)
            {
                throw new ConversionException("The PDF has no pages.");
            }

            var paths = PagePaths(output, pageCount);
            for (var i = 0; i < pageCount; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var page = document.GetPageReader(i);
                var pixels = page.GetImage(RenderFlags.RenderAnnotations);
                var read = new PixelReadSettings((uint)page.GetPageWidth(), (uint)page.GetPageHeight(), StorageType.Char, PixelMapping.BGRA);
                using var image = new MagickImage();
                image.ReadPixels(pixels, read);

                // PDFium leaves unpainted areas transparent; pages are meant to be seen on white paper.
                image.BackgroundColor = MagickColors.White;
                image.Alpha(AlphaAction.Remove);
                image.Density = new Density(s.PdfDpi);
                Process(image, format, s);

                written.Add(paths[i]);
                image.Write(paths[i], ToMagickFormat(format));
                progress.Report((i + 1.0) / pageCount);
            }
        }
        catch (Exception e)
        {
            written.ForEach(DeleteQuietly);
            throw e switch
            {
                DocnetException or MagickException => new ConversionException(e.Message, e),
                _ => e,
            };
        }
    }

    private static void CombineToPdf(IReadOnlyList<string> inputs, string output, ConversionSettings s, IProgress<double?> progress, CancellationToken cancellationToken)
    {
        progress.Report(0);
        try
        {
            // Every page is decoded before writing; fine for scans and photos, heavy for hundreds of large images.
            using var pages = new MagickImageCollection();
            for (var i = 0; i < inputs.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var page = new MagickImage(inputs[i]);
                pages.Add(page);
                Process(page, OutputFormat.Pdf, s);
                PrepareForPdf(page, inputs[i]);
                // Writing takes about as long as reading, so reading is the first half.
                progress.Report((i + 1.0) / inputs.Count / 2);
            }

            cancellationToken.ThrowIfCancellationRequested();
            pages.Write(output, MagickFormat.Pdf);
        }
        catch (MagickException e)
        {
            DeleteQuietly(output);
            throw new ConversionException(e.Message, e);
        }

        progress.Report(1);
    }

    /// <summary>Applies orientation, rotation, scaling and quality settings.</summary>
    private static void Process(MagickImage image, OutputFormat format, ConversionSettings s)
    {
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

        if (s.StripMetadata)
        {
            var colors = image.GetColorProfile();
            image.Strip();
            if (colors is not null)
            {
                image.SetProfile(colors);
            }
        }

        // Removing metadata from a JPEG keeps its own quality, so the picture loses as little as possible.
        var keepQuality = s.StripMetadata && format == OutputFormat.Jpg && image.Format == MagickFormat.Jpeg && image.Quality > 0;
        if (!keepQuality)
        {
            image.Quality = (uint)Math.Clamp(s.ImageQuality, 1, 100);
        }

        if (format == OutputFormat.Jpg)
        {
            // JPEG has no alpha channel; flatten onto white instead of black.
            image.BackgroundColor = MagickColors.White;
            image.Alpha(AlphaAction.Remove);
        }
    }

    /// <summary>
    /// The PDF page size is pixels / density. Photos often carry 72 dpi or none, which would make a page over a metre
    /// wide, so the density is raised until the longer side fits A4. Pixels are kept as they are.
    /// </summary>
    private static void PrepareForPdf(MagickImage image, string input)
    {
        const double A4LongSideInches = 297 / 25.4;
        const double DefaultDpi = 72;

        var dpi = image.Density.Units == DensityUnit.PixelsPerCentimeter ? image.Density.X * 2.54 : image.Density.X;
        if (dpi <= 0)
        {
            dpi = DefaultDpi;
        }

        var fitA4 = Math.Max(image.Width, image.Height) / A4LongSideInches;
        image.Density = new Density(Math.Max(dpi, fitA4), DensityUnit.PixelsPerInch);

        // Keep JPEG photos as JPEG inside the PDF; lossless sources stay lossless.
        var ext = Formats.ExtensionOf(input);
        image.Settings.Compression = ext is "jpg" or "jpeg" or "jfif" ? CompressionMethod.JPEG : CompressionMethod.Zip;
    }

    private static void DeleteQuietly(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static MagickFormat ToMagickFormat(OutputFormat format) => format switch
    {
        OutputFormat.Png => MagickFormat.Png,
        OutputFormat.Jpg => MagickFormat.Jpeg,
        OutputFormat.Webp => MagickFormat.WebP,
        OutputFormat.Avif => MagickFormat.Avif,
        OutputFormat.Ico => MagickFormat.Ico,
        OutputFormat.Pdf => MagickFormat.Pdf,
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Not an image format."),
    };
}
