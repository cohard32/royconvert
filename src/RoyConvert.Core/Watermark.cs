using ImageMagick;
using ImageMagick.Drawing;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace RoyConvert.Core;

public enum WatermarkPosition
{
    /// <summary>Once, across the middle along the diagonal.</summary>
    Diagonal,
    BottomRight,
    /// <summary>Repeated over the whole page at an angle.</summary>
    Tiled,
}

/// <param name="Opacity">0..1; the text is grey, so it shows on light and dark pictures.</param>
/// <param name="Size">Text height as a fraction of the shorter side of the picture or page.</param>
public sealed record WatermarkOptions(string Text, WatermarkPosition Position = WatermarkPosition.Diagonal, double Opacity = 0.4, double Size = 0.08);

/// <summary>
/// Writes text over pictures and PDF pages. The output is a new file next to the input; a PDF keeps its text and
/// vector content, the watermark is drawn on top of each page.
/// </summary>
public static class Watermark
{
    /// <summary>Pictures that can be marked: not animations, icons or vector drawings, which would lose what they are.</summary>
    public static readonly IReadOnlySet<string> Inputs = new HashSet<string>(
        Formats.ImageInputs.Except(["gif", "ico", "svg", "exr"]).Append("pdf"), StringComparer.OrdinalIgnoreCase);

    /// <summary>Kept as they are; other pictures (camera raw, HEIC, PSD, TGA) are written as JPEG.</summary>
    private static readonly IReadOnlySet<string> KeptImageFormats = new HashSet<string>(
        ["avif", "bmp", "jfif", "jpeg", "jpg", "png", "tif", "tiff", "webp"], StringComparer.OrdinalIgnoreCase);

    private const byte Grey = 128;
    private const string FontFamily = "Arial";
    private static readonly string? FontFile = FindFontFile();

    /// <summary>"{dir}/{name}{suffix}.{ext}", numbered if it exists; JPEG for pictures that are not kept as they are.</summary>
    public static string OutputPathFor(string input, string suffix, Func<string, bool>? exists = null)
    {
        exists ??= Path.Exists;
        var extension = Formats.ExtensionOf(input);
        if (extension != "pdf" && !KeptImageFormats.Contains(extension))
        {
            extension = "jpg";
        }

        var stem = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(input))!, Path.GetFileNameWithoutExtension(input) + suffix);
        var candidate = $"{stem}.{extension}";
        for (var i = 1; exists(candidate); i++)
        {
            candidate = $"{stem} ({i}).{extension}";
        }

        return candidate;
    }

    /// <exception cref="ConversionException">The file cannot be read or written.</exception>
    public static void Apply(string input, string output, WatermarkOptions options)
    {
        try
        {
            if (Formats.IsPdf(input))
            {
                ApplyToPdf(input, output, options);
            }
            else
            {
                using var image = new MagickImage(input);
                image.AutoOrient();
                Draw(image, options);
                image.Write(output, Formats.ExtensionOf(output) == "jpg" ? MagickFormat.Jpeg : image.Format);
            }
        }
        catch (Exception e) when (e is MagickException or IOException or UnauthorizedAccessException or InvalidOperationException
                                      or PdfiumException or PdfSharp.Pdf.IO.PdfReaderException)
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

    /// <summary>
    /// The picture, or the first page of the PDF, made to fit <paramref name="maxSize"/> pixels: what
    /// <see cref="Draw"/> marks for a preview, at the same proportions as the real output.
    /// </summary>
    /// <exception cref="ConversionException">The file cannot be read.</exception>
    public static MagickImage LoadPreview(string input, uint maxSize)
    {
        try
        {
            if (Formats.IsPdf(input))
            {
                using var pdf = PdfPages.Open(input);
                var (width, height) = pdf.PageSize(0);
                var scale = maxSize / Math.Max(width, height);
                var (w, h) = ((uint)Math.Max(1, width * scale), (uint)Math.Max(1, height * scale));
                var page = new MagickImage();
                page.ReadPixels(pdf.Render(0, (int)w, (int)h), new PixelReadSettings(w, h, StorageType.Char, PixelMapping.BGRA));
                return page;
            }

            var image = new MagickImage(input);
            image.AutoOrient();
            image.Thumbnail(new MagickGeometry(maxSize, maxSize) { Greater = true });
            return image;
        }
        catch (Exception e) when (e is MagickException or IOException or UnauthorizedAccessException or PdfiumException)
        {
            throw new ConversionException(e.Message, e);
        }
    }

    /// <summary>Draws the watermark onto the picture in place; also used for the preview.</summary>
    public static void Draw(MagickImage image, WatermarkOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.Text))
        {
            return;
        }

        var fontSize = FontSize(image.Width, image.Height, options);
        var (textWidth, textHeight) = Measure(options.Text, fontSize);
        var layout = Layout(image.Width, image.Height, textWidth, textHeight, options);
        fontSize *= layout.Scale;
        (textWidth, textHeight) = Measure(options.Text, fontSize);

        // The text once, on a transparent label that is turned and then placed at each spot.
        var padding = Math.Ceiling(fontSize * 0.2);
        using var label = new MagickImage(MagickColors.Transparent, (uint)Math.Ceiling(textWidth + 2 * padding),
            (uint)Math.Ceiling(textHeight + 2 * padding));
        var alpha = (byte)Math.Round(Math.Clamp(options.Opacity, 0, 1) * 255);
        var drawables = new Drawables().FontPointSize(fontSize)
            .FillColor(MagickColor.FromRgba(Grey, Grey, Grey, alpha))
            .TextAlignment(TextAlignment.Center)
            .Text(label.Width / 2.0, padding + Ascent(fontSize), options.Text);
        drawables = FontFile is null ? drawables : drawables.Font(FontFile);
        drawables.Draw(label);
        if (layout.Angle != 0)
        {
            label.BackgroundColor = MagickColors.Transparent;
            label.Rotate(-layout.Angle);
        }

        foreach (var (x, y) in layout.Centers)
        {
            image.Composite(label, (int)Math.Round(x - label.Width / 2.0), (int)Math.Round(y - label.Height / 2.0), CompositeOperator.Over);
        }
    }

    private static void ApplyToPdf(string input, string output, WatermarkOptions options)
    {
        // PDFium names a damaged or protected file clearly; PDFsharp throws whatever its parser hits.
        PdfPages.Open(input).Dispose();
        if (OperatingSystem.IsWindows())
        {
            GlobalFontSettings.UseWindowsFontsUnderWindows = true;
        }

        using var document = PdfReader.Open(input, PdfDocumentOpenMode.Modify);
        // Transparency needs PDF 1.4; PDFsharp draws opaque into older files.
        document.Version = Math.Max(document.Version, 14);
        var alpha = (int)Math.Round(Math.Clamp(options.Opacity, 0, 1) * 255);
        var brush = new XSolidBrush(XColor.FromArgb(alpha, Grey, Grey, Grey));
        foreach (var page in document.Pages)
        {
            using var graphics = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);
            var (width, height) = (graphics.PageSize.Width, graphics.PageSize.Height);
            // A turned page (/Rotate) is shown turned; draw in the coordinates of what the reader sees.
            switch ((page.Rotate % 360 + 360) % 360)
            {
                case 90:
                    graphics.TranslateTransform(0, height);
                    graphics.RotateTransform(-90);
                    (width, height) = (height, width);
                    break;
                case 180:
                    graphics.TranslateTransform(width, height);
                    graphics.RotateTransform(180);
                    break;
                case 270:
                    graphics.TranslateTransform(width, 0);
                    graphics.RotateTransform(90);
                    (width, height) = (height, width);
                    break;
            }

            var fontSize = FontSize(width, height, options);
            var size = graphics.MeasureString(options.Text, Font(fontSize));
            var layout = Layout(width, height, size.Width, size.Height, options);
            var font = Font(fontSize * layout.Scale);
            foreach (var (x, y) in layout.Centers)
            {
                var state = graphics.Save();
                graphics.TranslateTransform(x, y);
                graphics.RotateTransform(-layout.Angle);
                graphics.DrawString(options.Text, font, brush, 0, 0, XStringFormats.Center);
                graphics.Restore(state);
            }
        }

        document.Save(output);
    }

    private static XFont Font(double size) =>
        new(FontFamily, size, XFontStyleEx.Bold, new XPdfFontOptions(PdfFontEncoding.Unicode));

    private static double FontSize(double width, double height, WatermarkOptions options) =>
        Math.Max(1, Math.Min(width, height) * Math.Clamp(options.Size, 0.01, 1));

    /// <param name="Scale">Applied to the font size so the text fits.</param>
    /// <param name="Angle">Degrees, counter-clockwise.</param>
    internal sealed record Placement(double Scale, double Angle, IReadOnlyList<(double X, double Y)> Centers);

    /// <summary>Where the text goes on a <paramref name="width"/> × <paramref name="height"/> page, given its size.</summary>
    internal static Placement Layout(double width, double height, double textWidth, double textHeight, WatermarkOptions options)
    {
        var shorter = Math.Min(width, height);
        switch (options.Position)
        {
            case WatermarkPosition.BottomRight:
            {
                var margin = shorter * 0.03;
                var scale = Math.Min(1, (width - 2 * margin) / textWidth);
                return new Placement(scale, 0,
                    [(width - margin - textWidth * scale / 2, height - margin - textHeight * scale / 2)]);
            }

            case WatermarkPosition.Tiled:
            {
                const double angle = 30;
                var scale = Math.Min(1, width * 0.8 / textWidth);
                // The grid follows the text: along each line the copies sit end to end with a gap, the lines are
                // apart across it, and every other line is shifted by half a step.
                var along = textWidth * scale + textHeight * scale * 2;
                var across = textHeight * scale * 4;
                var radians = angle * Math.PI / 180;
                var (ux, uy) = (Math.Cos(radians), -Math.Sin(radians));
                var (vx, vy) = (-uy, ux);
                // Enough lines and copies to cover the page from its middle, then keep those that touch it.
                var reach = Math.Sqrt(width * width + height * height) / 2 + textWidth * scale;
                var (lines, copies) = ((int)Math.Ceiling(reach / across), (int)Math.Ceiling(reach / along) + 1);
                var centers = new List<(double, double)>();
                for (var line = -lines; line <= lines; line++)
                {
                    for (var copy = -copies; copy <= copies; copy++)
                    {
                        var u = (copy + (line % 2 == 0 ? 0 : 0.5)) * along;
                        var v = line * across;
                        var (x, y) = (width / 2 + u * ux + v * vx, height / 2 + u * uy + v * vy);
                        var margin = textWidth * scale / 2;
                        if (x > -margin && x < width + margin && y > -margin && y < height + margin)
                        {
                            centers.Add((x, y));
                        }
                    }
                }

                return new Placement(scale, angle, centers);
            }

            default:
            {
                var angle = Math.Atan2(height, width) * 180 / Math.PI;
                var diagonal = Math.Sqrt(width * width + height * height);
                var scale = Math.Min(1, diagonal * 0.8 / textWidth);
                return new Placement(scale, angle, [(width / 2, height / 2)]);
            }
        }
    }

    private static (double Width, double Height) Measure(string text, double fontSize)
    {
        using var probe = new MagickImage(MagickColors.Transparent, 1, 1);
        probe.Settings.FontPointsize = fontSize;
        if (FontFile is not null)
        {
            probe.Settings.Font = FontFile;
        }

        var metrics = probe.FontTypeMetrics(text)!;
        return (metrics.TextWidth, metrics.Ascent - metrics.Descent);
    }

    private static double Ascent(double fontSize)
    {
        using var probe = new MagickImage(MagickColors.Transparent, 1, 1);
        probe.Settings.FontPointsize = fontSize;
        if (FontFile is not null)
        {
            probe.Settings.Font = FontFile;
        }

        return probe.FontTypeMetrics("Hg")!.Ascent;
    }

    /// <summary>Arial Bold, the same font as in PDFs; ImageMagick's default font lacks letters such as "ğ".</summary>
    private static string? FindFontFile()
    {
        var fonts = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
        return new[] { Path.Combine(fonts, "arialbd.ttf"), "/System/Library/Fonts/Supplemental/Arial Bold.ttf" }
            .FirstOrDefault(File.Exists);
    }
}
