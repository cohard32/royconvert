using System.Text;
using Docnet.Core;
using Docnet.Core.Models;
using ImageMagick;

namespace RoyConvert.Core.Tests;

/// <summary>Merging PDFs (with images) into one and splitting a PDF into one file per page.</summary>
public sealed class PdfPagesTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("royconvert-tests-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public async Task Split_WritesOnePdfPerPage_AndKeepsTheText()
    {
        var input = Path.Combine(_dir, "rapor.pdf");
        CreateTextPdf(input, "Page one", "Page two", "Page three");
        var job = new ConversionJob(input, PresetCatalog.Find(PresetCatalog.Default, "split-pdf")!);

        using var runner = ConversionRunner.CreateDefault(null, 1);
        await runner.RunAsync([job]);

        Assert.True(job.State == JobState.Done, job.Error);
        string[] expected = ["rapor-page-01.pdf", "rapor-page-02.pdf", "rapor-page-03.pdf"];
        Assert.Equal(expected.Append("rapor.pdf").Order(), Directory.GetFiles(_dir).Select(Path.GetFileName).Order());
        Assert.Equal(["Page one"], Texts(Path.Combine(_dir, expected[0])));
        Assert.Equal(["Page three"], Texts(Path.Combine(_dir, expected[2])));
    }

    [Fact]
    public async Task Split_OfASinglePage_FailsWithoutOutput()
    {
        var input = Path.Combine(_dir, "one.pdf");
        CreateTextPdf(input, "Only page");
        var job = new ConversionJob(input, PresetCatalog.Find(PresetCatalog.Default, "split-pdf")!);

        using var runner = ConversionRunner.CreateDefault(null, 1);
        await runner.RunAsync([job]);

        Assert.Equal(JobState.Failed, job.State);
        Assert.Contains("only one page", job.Error);
        Assert.Equal(["one.pdf"], Directory.GetFiles(_dir).Select(Path.GetFileName));
    }

    [Fact]
    public async Task Combine_MergesPdfsAndImages_InTheGivenOrder()
    {
        var first = Path.Combine(_dir, "a.pdf");
        var photo = Path.Combine(_dir, "photo.png");
        var last = Path.Combine(_dir, "b.pdf");
        CreateTextPdf(first, "A1", "A2");
        using (var image = new MagickImage(MagickColors.CornflowerBlue, 200, 100))
        {
            image.Write(photo);
        }

        CreateTextPdf(last, "B1");
        var job = new ConversionJob([first, photo, last], PresetCatalog.Find(PresetCatalog.Default, "combine-pdf")!);

        using var runner = ConversionRunner.CreateDefault(null, 1);
        await runner.RunAsync([job]);

        Assert.True(job.State == JobState.Done, job.Error);
        Assert.Equal(Path.Combine(_dir, "a (combined).pdf"), job.Output);
        Assert.Equal(["A1", "A2", "", "B1"], Texts(job.Output!));
        using var document = DocLib.Instance.GetDocReader(job.Output!, new PageDimensions(1.0));
        using var imagePage = document.GetPageReader(2);
        Assert.InRange(imagePage.GetPageWidth(), 199, 201);
    }

    [Fact]
    public async Task Combine_WithADamagedPdf_NamesItAndWritesNothing()
    {
        var good = Path.Combine(_dir, "good.pdf");
        var bad = Path.Combine(_dir, "bad.pdf");
        CreateTextPdf(good, "fine");
        File.WriteAllText(bad, "not a pdf");
        var job = new ConversionJob([good, bad], PresetCatalog.Find(PresetCatalog.Default, "combine-pdf")!);

        using var runner = ConversionRunner.CreateDefault(null, 1);
        await runner.RunAsync([job]);

        Assert.Equal(JobState.Failed, job.State);
        Assert.Contains("bad.pdf", job.Error);
        Assert.Equal(["bad.pdf", "good.pdf"], Directory.GetFiles(_dir).Select(Path.GetFileName).Order());
    }

    [Fact]
    public void SplitPreset_IsOnlyOfferedForPdfs()
    {
        var split = PresetCatalog.Find(PresetCatalog.Default, "split-pdf")!;
        Assert.True(split.Accepts("x.PDF"));
        Assert.False(split.Accepts("x.png"));
        Assert.True(PresetCatalog.Find(PresetCatalog.Default, "combine-pdf")!.Accepts("x.pdf"));
    }

    private static string[] Texts(string path)
    {
        using var document = DocLib.Instance.GetDocReader(path, new PageDimensions(1.0));
        return Enumerable.Range(0, document.GetPageCount()).Select(i =>
        {
            using var page = document.GetPageReader(i);
            return page.GetText().Trim();
        }).ToArray();
    }

    /// <summary>A minimal PDF with one line of real (selectable) text per page.</summary>
    private static void CreateTextPdf(string path, params string[] pages)
    {
        var objects = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [" + string.Join(" ", pages.Select((_, i) => $"{4 + 2 * i} 0 R")) + $"] /Count {pages.Length} >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
        };
        for (var i = 0; i < pages.Length; i++)
        {
            var content = $"BT /F1 24 Tf 72 720 Td ({pages[i]}) Tj ET";
            objects.Add($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 3 0 R >> >> /Contents {5 + 2 * i} 0 R >>");
            objects.Add($"<< /Length {content.Length} >>\nstream\n{content}\nendstream");
        }

        var pdf = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int>();
        for (var i = 0; i < objects.Count; i++)
        {
            offsets.Add(pdf.Length);
            pdf.Append($"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
        }

        var xref = pdf.Length;
        pdf.Append($"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n");
        offsets.ForEach(o => pdf.Append($"{o:D10} 00000 n \n"));
        pdf.Append($"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        File.WriteAllText(path, pdf.ToString(), Encoding.ASCII);
    }
}
