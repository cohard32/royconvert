using ImageMagick;
using PdfSharp.Pdf.IO;

namespace RoyConvert.Core.Tests;

public sealed class WatermarkTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("royconvert-watermark-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string Photo(string name, uint width = 1200, uint height = 800)
    {
        var path = Path.Combine(_dir, name);
        using var image = new MagickImage(MagickColors.White, width, height);
        image.Write(path);
        return path;
    }

    /// <summary>A PDF with two plain pages, the second one turned (/Rotate 90).</summary>
    private string Pdf()
    {
        var path = Path.Combine(_dir, "belge.pdf");
        using (var pages = new MagickImageCollection())
        {
            pages.Add(new MagickImage(MagickColors.White, 842, 595));
            pages.Add(new MagickImage(MagickColors.White, 842, 595));
            pages.Write(path, MagickFormat.Pdf);
        }

        using var document = PdfReader.Open(path, PdfDocumentOpenMode.Modify);
        document.Pages[1].Rotate = 90;
        document.Save(path);
        return path;
    }

    [Fact]
    public void Inputs_ArePicturesAndPdf_NotAnimationsIconsOrDrawings()
    {
        Assert.Contains("jpg", Watermark.Inputs);
        Assert.Contains("heic", Watermark.Inputs);
        Assert.Contains("pdf", Watermark.Inputs);
        Assert.DoesNotContain("gif", Watermark.Inputs);
        Assert.DoesNotContain("ico", Watermark.Inputs);
        Assert.DoesNotContain("svg", Watermark.Inputs);
    }

    [Theory]
    [InlineData(@"C:\f\tatil.jpg", @"C:\f\tatil (filigranlı).jpg")]
    [InlineData(@"C:\f\Tarama.TIFF", @"C:\f\Tarama (filigranlı).tiff")]
    [InlineData(@"C:\f\telefon.heic", @"C:\f\telefon (filigranlı).jpg")]
    [InlineData(@"C:\f\belge.pdf", @"C:\f\belge (filigranlı).pdf")]
    public void OutputPath_KeepsCommonFormats_OthersBecomeJpeg(string input, string expected)
    {
        Assert.Equal(expected, Watermark.OutputPathFor(input, " (filigranlı)", _ => false));
    }

    [Fact]
    public void OutputPath_IsNumbered_WhenTaken()
    {
        var taken = new HashSet<string>([@"C:\f\a (filigranlı).png", @"C:\f\a (filigranlı) (1).png"]);

        Assert.Equal(@"C:\f\a (filigranlı) (2).png", Watermark.OutputPathFor(@"C:\f\a.png", " (filigranlı)", taken.Contains));
    }

    [Theory]
    [InlineData(WatermarkPosition.Diagonal, 600, 400)]
    [InlineData(WatermarkPosition.BottomRight, 1100, 760)]
    [InlineData(WatermarkPosition.Tiled, 600, 400)]
    public void Picture_GetsGreyTextWhereAsked_SameSize_SourceUnchanged(WatermarkPosition position, int x, int y)
    {
        var input = Photo("foto.png");
        var before = File.ReadAllBytes(input);
        var output = Path.Combine(_dir, "out.png");

        Watermark.Apply(input, output, new WatermarkOptions("Gizli ğüşİ", position, Opacity: 1, Size: 0.1));

        using var image = new MagickImage(output);
        Assert.Equal((1200u, 800u), (image.Width, image.Height));
        Assert.Equal(before, File.ReadAllBytes(input));
        // Some grey pixels near the expected spot; the rest of a white page stays white.
        using var pixels = image.GetPixels();
        var area = Enumerable.Range(-60, 120).SelectMany(dx => Enumerable.Range(-30, 60).Select(dy => pixels.GetPixel(x + dx, y + dy)!.ToColor()!));
        Assert.Contains(area, c => c.R < 200 * 257);
        Assert.Equal(MagickColors.White, pixels.GetPixel(5, 5)!.ToColor());
    }

    [Fact]
    public void Picture_OpacityMakesTheTextLighter()
    {
        var input = Photo("foto.jpg");

        double Darkest(double opacity)
        {
            var output = Path.Combine(_dir, $"out-{opacity}.png");
            Watermark.Apply(input, output, new WatermarkOptions("XXXX", WatermarkPosition.Diagonal, opacity, 0.2));
            using var image = new MagickImage(output);
            using var pixels = image.GetPixels();
            return Enumerable.Range(500, 200).Select(x => pixels.GetPixel(x, 400)!.ToColor()!.R).Min();
        }

        Assert.True(Darkest(0.2) > Darkest(0.9));
    }

    [Fact]
    public void Pdf_EveryPageGetsTheText_AsText_WithTurkishLetters()
    {
        var input = Pdf();
        var output = Path.Combine(_dir, "out.pdf");

        Watermark.Apply(input, output, new WatermarkOptions("TASLAK ğüşİ", WatermarkPosition.Tiled));

        using var pdf = PdfiumDocument.Open(output);
        Assert.Equal(2, pdf.PageCount);
        Assert.Contains("TASLAK ğüşİ", pdf.Text(0));
        Assert.Contains("TASLAK ğüşİ", pdf.Text(1));
        // Transparency needs PDF 1.4.
        using var saved = PdfReader.Open(output, PdfDocumentOpenMode.Import);
        Assert.True(saved.Version >= 14);
    }

    [Fact]
    public void Pdf_TurnedPage_GetsTheTextAcrossWhatTheReaderSees()
    {
        var input = Pdf();
        var output = Path.Combine(_dir, "out.pdf");

        Watermark.Apply(input, output, new WatermarkOptions("XXXXXXXX", WatermarkPosition.BottomRight, Opacity: 1, Size: 0.1));

        // Page 2 is shown 595 × 842 (portrait): the text must be in its visible bottom right corner.
        using var pdf = PdfiumDocument.Open(output);
        var (width, height) = pdf.PageSize(1);
        Assert.True(height > width);
        var pixels = pdf.Render(1, (int)width, (int)height);
        bool Dark(int x, int y) => pixels[(y * (int)width + x) * 4] < 200;
        Assert.Contains(Enumerable.Range((int)width - 150, 120), x => Dark(x, (int)height - 40));
        Assert.DoesNotContain(Enumerable.Range(20, 120), x => Dark(x, (int)height - 40));
    }

    [Theory]
    [InlineData(1200, 800)]
    [InlineData(595, 842)]
    public void Tiled_CopiesDoNotOverlap_AndCoverThePage(double width, double height)
    {
        const double textWidth = 400, textHeight = 40;
        var layout = Watermark.Layout(width, height, textWidth, textHeight, new WatermarkOptions("x", WatermarkPosition.Tiled));

        var radians = layout.Angle * Math.PI / 180;
        var (ux, uy) = (Math.Cos(radians), -Math.Sin(radians));
        var (w, h) = (textWidth * layout.Scale, textHeight * layout.Scale);
        foreach (var a in layout.Centers)
        {
            foreach (var b in layout.Centers.Where(b => b != a))
            {
                // In the text's own direction: two copies on the same line must not touch.
                var (dx, dy) = (b.X - a.X, b.Y - a.Y);
                var along = Math.Abs(dx * ux + dy * uy);
                var across = Math.Abs(-dx * uy + dy * ux);
                Assert.True(across >= h || along >= w, $"{a} and {b} overlap");
            }
        }

        // Every corner region has a copy nearby.
        foreach (var (x, y) in new[] { (0.0, 0.0), (width, 0), (0, height), (width, height), (width / 2, height / 2) })
        {
            Assert.Contains(layout.Centers, c => Math.Abs(c.X - x) < width / 2 && Math.Abs(c.Y - y) < height / 2);
        }
    }

    [Fact]
    public void BrokenFile_IsAConversionError_AndLeavesNoOutput()
    {
        var input = Path.Combine(_dir, "bozuk.pdf");
        File.WriteAllText(input, "not a pdf");
        var output = Path.Combine(_dir, "out.pdf");

        Assert.Throws<ConversionException>(() => Watermark.Apply(input, output, new WatermarkOptions("x")));
        Assert.False(File.Exists(output));
    }

    [Fact]
    public void Preview_FitsTheSize_ForPicturesAndPdfs()
    {
        using var picture = Watermark.LoadPreview(Photo("foto.jpg"), 300);
        using var page = Watermark.LoadPreview(Pdf(), 300);

        Assert.Equal((300u, 200u), (picture.Width, picture.Height));
        Assert.Equal(300u, Math.Max(page.Width, page.Height));
    }
}
