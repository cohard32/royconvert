using ImageMagick;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace RoyConvert.Core;

/// <summary>
/// Makes PDFs smaller by recompressing their images, which is where the size of scans and photo documents is.
/// Text, fonts and vector graphics are not touched. Each image is shrunk to <see cref="ConversionSettings.PdfDpi"/> on
/// the largest page and stored as JPEG at <see cref="ConversionSettings.ImageQuality"/>, but only when that makes it
/// smaller; lossless images (screenshots, drawings) only when it halves them, so they stay sharp.
/// </summary>
public static class PdfCompression
{
    /// <summary>A JPEG is only replaced when the new one is at least this much smaller.</summary>
    private const double JpegGain = 0.9;

    public static void Compress(string input, string output, ConversionSettings settings, IProgress<double?> progress, CancellationToken cancellationToken)
    {
        // PDFium names a damaged or protected file clearly; PDFsharp throws whatever its parser hits.
        PdfPages.Open(input).Dispose();
        progress.Report(0);
        try
        {
            using var document = PdfReader.Open(input, PdfDocumentOpenMode.Modify);
            var longestSide = document.Pages.Cast<PdfPage>().Max(p => Math.Max(p.Width.Point, p.Height.Point));
            var maxPixels = (int)Math.Ceiling(longestSide / 72 * settings.PdfDpi);

            var images = document.Internals.GetAllObjects().OfType<PdfDictionary>()
                .Where(d => d.Stream is not null && d.Elements.GetName("/Subtype") == "/Image")
                .ToList();
            // Soft masks are an image's transparency; JPEG artefacts would show at its edges.
            var masks = images.Select(i => i.Elements.GetReference("/SMask")?.Value).OfType<PdfObject>()
                .ToHashSet(ReferenceEqualityComparer.Instance);
            for (var i = 0; i < images.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!masks.Contains(images[i]))
                {
                    Recompress(images[i], maxPixels, settings.ImageQuality);
                }

                // Saving takes a moment too; leave the last part of the bar for it.
                progress.Report((i + 1.0) / images.Count * 0.9);
            }

            cancellationToken.ThrowIfCancellationRequested();
            document.Options.CompressContentStreams = true;
            document.Options.UseFlateDecoderForJpegImages = PdfUseFlateDecoderForJpegImages.Never;
            document.Save(output);
        }
        catch (Exception e) when (e is not (OperationCanceledException or IOException or UnauthorizedAccessException or ConversionException))
        {
            DeleteQuietly(output);
            throw new ConversionException(
                $"{Path.GetFileName(input)} could not be compressed. It may be damaged or password protected. ({e.Message})", e);
        }
        catch
        {
            DeleteQuietly(output);
            throw;
        }

        if (new FileInfo(output).Length >= new FileInfo(input).Length)
        {
            DeleteQuietly(output);
            throw new ConversionException("The PDF could not be made smaller (it has no large images or they are already compressed).");
        }

        progress.Report(1);
    }

    /// <summary>Replaces the image's data with a smaller JPEG; leaves images it cannot read safely as they are.</summary>
    private static void Recompress(PdfDictionary image, int maxPixels, int quality)
    {
        var width = image.Elements.GetInteger("/Width");
        var height = image.Elements.GetInteger("/Height");
        var channels = Name(image.Elements["/ColorSpace"]) switch
        {
            "/DeviceRGB" => 3,
            "/DeviceGray" => 1,
            _ => 0, // CMYK, indexed, ICC based…: colours could shift
        };
        if (channels == 0 || width <= 0 || height <= 0 || image.Elements.GetInteger("/BitsPerComponent") != 8
            || image.Elements.ContainsKey("/Decode") || image.Elements.ContainsKey("/Mask"))
        {
            return;
        }

        var filter = Resolve(image.Elements["/Filter"]) is PdfArray { Elements.Count: 1 } array
            ? Name(array.Elements[0])
            : Name(image.Elements["/Filter"]);

        MagickImage picture;
        if (filter == "/DCTDecode")
        {
            picture = new MagickImage(image.Stream.Value, new MagickReadSettings { Format = MagickFormat.Jpeg });
            if (picture.Width != width || picture.Height != height || picture.ColorSpace == ColorSpace.CMYK)
            {
                picture.Dispose();
                return;
            }
        }
        else if (filter == "/FlateDecode" && !image.Elements.ContainsKey("/DecodeParms"))
        {
            var pixels = image.Stream.UnfilteredValue;
            if (pixels.Length != width * height * channels)
            {
                return;
            }

            picture = new MagickImage(pixels, new MagickReadSettings
            {
                Width = (uint)width,
                Height = (uint)height,
                Depth = 8,
                Format = channels == 3 ? MagickFormat.Rgb : MagickFormat.Gray,
            });
        }
        else
        {
            return;
        }

        using (picture)
        {
            picture.Resize(new MagickGeometry((uint)maxPixels, (uint)maxPixels) { Greater = true });
            picture.Strip();
            picture.Quality = (uint)Math.Clamp(quality, 1, 100);
            if (channels == 1)
            {
                picture.ColorType = ColorType.Grayscale;
            }

            var jpeg = picture.ToByteArray(MagickFormat.Jpeg);
            var stored = image.Stream.Value.Length;
            if (jpeg.Length >= (filter == "/DCTDecode" ? stored * JpegGain : stored / 2.0))
            {
                return;
            }

            image.Stream.Value = jpeg;
            image.Elements.SetName("/Filter", "/DCTDecode");
            image.Elements.Remove("/DecodeParms");
            image.Elements.SetInteger("/Width", (int)picture.Width);
            image.Elements.SetInteger("/Height", (int)picture.Height);
            image.Elements.SetInteger("/Length", jpeg.Length);
        }
    }

    /// <summary>Values can be stored elsewhere in the file ("10 0 R"), e.g. ImageMagick writes colour spaces that way.</summary>
    private static PdfItem? Resolve(PdfItem? item) => item is PdfSharp.Pdf.Advanced.PdfReference reference ? reference.Value : item;

    /// <summary>A name written in place (PdfName) or as its own object (PdfNameObject); null for anything else.</summary>
    private static string? Name(PdfItem? item) => Resolve(item) switch
    {
        PdfName name => name.Value,
        PdfNameObject name => name.Value,
        _ => null,
    };

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
}
