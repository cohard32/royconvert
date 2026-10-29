using Docnet.Core;
using Docnet.Core.Models;
using ImageMagick;

namespace RoyConvert.Core.Tests;

/// <summary>"PDF (compress)": images inside the PDF are shrunk and recompressed, everything else stays.</summary>
public sealed class PdfCompressionTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("royconvert-tests-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static Preset Compress => PresetCatalog.Find(PresetCatalog.Default, "compress-pdf")!;

    [Theory]
    [InlineData("fotograf.jpg")] // photos stay JPEG inside the PDF
    [InlineData("tarama.png")]   // lossless scans are stored with Flate
    public async Task PdfWithLargeImages_BecomesMuchSmaller_WithTheSamePages(string imageName)
    {
        var pdf = await CreatePhotoPdf(imageName);
        var source = File.ReadAllBytes(pdf);
        var job = new ConversionJob(pdf, Compress);

        using var runner = ConversionRunner.CreateDefault(null, 1);
        await runner.RunAsync([job]);

        Assert.True(job.State == JobState.Done, job.Error);
        Assert.Equal(Path.Combine(_dir, "belge (compressed).pdf"), job.Output);
        var size = new FileInfo(job.Output!).Length;
        Assert.True(size < source.Length / 3, $"{source.Length} → {size} bytes");
        Assert.Equal(source, File.ReadAllBytes(pdf));
        using var before = DocLib.Instance.GetDocReader(pdf, new PageDimensions(1.0));
        using var after = DocLib.Instance.GetDocReader(job.Output!, new PageDimensions(1.0));
        Assert.Equal(before.GetPageCount(), after.GetPageCount());
        using var page = after.GetPageReader(0);
        Assert.Equal((before.GetPageReader(0).GetPageWidth(), before.GetPageReader(0).GetPageHeight()), (page.GetPageWidth(), page.GetPageHeight()));
        // The picture is still there: the rendered page is not blank.
        Assert.Contains(page.GetImage().Where((_, i) => i % 4 == 3), alpha => alpha != 0);
    }

    [Fact]
    public async Task PdfWithoutImages_SaysItCannotBeMadeSmaller_AndWritesNothing()
    {
        var pdf = Path.Combine(_dir, "metin.pdf");
        File.WriteAllText(pdf, TextPdf());
        var job = new ConversionJob(pdf, Compress);

        using var runner = ConversionRunner.CreateDefault(null, 1);
        await runner.RunAsync([job]);

        Assert.Equal(JobState.Failed, job.State);
        Assert.Contains("could not be made smaller", job.Error);
        Assert.Equal(["metin.pdf"], Directory.GetFiles(_dir).Select(Path.GetFileName));
    }

    [Fact]
    public async Task DamagedPdf_FailsWithItsName()
    {
        var pdf = Path.Combine(_dir, "bozuk.pdf");
        File.WriteAllText(pdf, "%PDF-1.4 not really");
        var job = new ConversionJob(pdf, Compress);

        using var runner = ConversionRunner.CreateDefault(null, 1);
        await runner.RunAsync([job]);

        Assert.Equal(JobState.Failed, job.State);
        Assert.Contains("bozuk.pdf", job.Error);
        Assert.Equal(["bozuk.pdf"], Directory.GetFiles(_dir).Select(Path.GetFileName));
    }

    [Fact]
    public void IsOnlyOfferedForPdfs()
    {
        Assert.True(Compress.Accepts("a.PDF"));
        Assert.False(Compress.Accepts("a.jpg"));
    }

    /// <summary>A 2-page A4 PDF made by royconvert from a large, detailed image (noise compresses badly).</summary>
    private async Task<string> CreatePhotoPdf(string imageName)
    {
        var image = Path.Combine(_dir, imageName);
        using (var picture = new MagickImage("plasma:", 3000, 2000))
        {
            picture.Quality = 95;
            picture.Write(image);
        }

        var job = new ConversionJob([image, image], PresetCatalog.Find(PresetCatalog.Default, "combine-pdf")!);
        using (var runner = ConversionRunner.CreateDefault(null, 1))
        {
            await runner.RunAsync([job]);
        }

        Assert.True(job.State == JobState.Done, job.Error);
        var pdf = Path.Combine(_dir, "belge.pdf");
        File.Move(job.Output!, pdf);
        File.Delete(image);
        return pdf;
    }

    /// <summary>A one-page PDF with a line of text and no images, with a valid cross-reference table.</summary>
    private static string TextPdf()
    {
        const string content = "BT /F1 24 Tf 72 720 Td (Merhaba) Tj ET";
        string[] objects =
        [
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
            $"<< /Length {content.Length} >>\nstream\n{content}\nendstream",
        ];
        var pdf = new System.Text.StringBuilder("%PDF-1.4\n");
        var offsets = new List<int>();
        for (var i = 0; i < objects.Length; i++)
        {
            offsets.Add(pdf.Length);
            pdf.Append($"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
        }

        var xref = pdf.Length;
        pdf.Append($"xref\n0 {objects.Length + 1}\n0000000000 65535 f \n");
        offsets.ForEach(o => pdf.Append($"{o:D10} 00000 n \n"));
        pdf.Append($"trailer\n<< /Size {objects.Length + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return pdf.ToString();
    }
}
