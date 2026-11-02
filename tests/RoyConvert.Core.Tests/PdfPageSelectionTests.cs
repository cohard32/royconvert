namespace RoyConvert.Core.Tests;

public sealed class PdfPageSelectionTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("royconvert-pages-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string TenPages()
    {
        var path = Path.Combine(_dir, "belge ğ.pdf");
        PdfPagesTests.CreateTextPdf(path, Enumerable.Range(1, 10).Select(i => $"P{i}").ToArray());
        return path;
    }

    [Theory]
    [InlineData("1-3, 5, 8-", new[] { 0, 1, 2, 4, 7, 8, 9 })]
    [InlineData("5", new[] { 4 })]
    [InlineData("3,1,2,3", new[] { 0, 1, 2 })]   // document order, each once
    [InlineData("1 - 3", new[] { 0, 1, 2 })]
    [InlineData("2–4", new[] { 1, 2, 3 })]       // en dash
    [InlineData("1;10", new[] { 0, 9 })]
    [InlineData("10-", new[] { 9 })]
    public void Parse_Keep(string text, int[] expected)
    {
        var range = PdfPageSelection.Parse(text, 10, delete: false);
        Assert.True(range.IsValid, range.Problem.ToString());
        Assert.Equal(expected, range.Pages);
    }

    [Fact]
    public void Parse_Delete_GivesThePagesLeft()
    {
        Assert.Equal([1, 2, 3, 4, 5, 6, 7, 8], PdfPageSelection.Parse("1, 10", 10, delete: true).Pages);
        Assert.Equal(PageRangeProblem.NothingLeft, PdfPageSelection.Parse("1-", 10, delete: true).Problem);
    }

    [Theory]
    [InlineData("", PageRangeProblem.Empty)]
    [InlineData("  ,  ", PageRangeProblem.Empty)]
    [InlineData("0", PageRangeProblem.Invalid)]
    [InlineData("a", PageRangeProblem.Invalid)]
    [InlineData("5-3", PageRangeProblem.Invalid)]
    [InlineData("-3", PageRangeProblem.Invalid)]
    [InlineData("1.5", PageRangeProblem.Invalid)]
    [InlineData("11", PageRangeProblem.PastTheEnd)]
    [InlineData("8-12", PageRangeProblem.PastTheEnd)]
    [InlineData("12-", PageRangeProblem.PastTheEnd)]
    public void Parse_Problems(string text, PageRangeProblem problem)
    {
        Assert.Equal(problem, PdfPageSelection.Parse(text, 10, delete: false).Problem);
    }

    [Fact]
    public void Parse_PastTheEnd_NamesTheFirstMissingPage()
    {
        Assert.Equal(12, PdfPageSelection.Parse("12", 10, false).FirstMissingPage);
        Assert.Equal(11, PdfPageSelection.Parse("8-15", 10, false).FirstMissingPage);
    }

    [Fact]
    public void Runs_GroupConsecutivePages()
    {
        Assert.Equal([(0, 2), (4, 4), (7, 9)], PdfPageSelection.Runs([0, 1, 2, 4, 7, 8, 9]));
    }

    [Fact]
    public void Apply_Keep_WritesTheChosenPages_WithTheirText()
    {
        var input = TenPages();
        var before = File.ReadAllBytes(input);
        var output = Path.Combine(_dir, "sonuc.pdf");

        PdfPageSelection.Apply(input, output, "1-3, 5, 8-", delete: false);

        Assert.Equal(["P1", "P2", "P3", "P5", "P8", "P9", "P10"], PdfPagesTests.Texts(output));
        Assert.Equal(before, File.ReadAllBytes(input));
        Assert.Equal(10, PdfPageSelection.PageCount(input));
    }

    [Fact]
    public void Apply_Delete_LeavesTheOtherPages()
    {
        var output = Path.Combine(_dir, "sonuc.pdf");

        PdfPageSelection.Apply(TenPages(), output, "2-9", delete: true);

        Assert.Equal(["P1", "P10"], PdfPagesTests.Texts(output));
    }

    [Fact]
    public void Apply_PagesPastTheEnd_FailsWithoutOutput()
    {
        var output = Path.Combine(_dir, "sonuc.pdf");

        var e = Assert.Throws<ConversionException>(() => PdfPageSelection.Apply(TenPages(), output, "9-12", delete: false));
        Assert.Contains("10", e.Message);
        Assert.False(File.Exists(output));
    }

    [Fact]
    public void Apply_DamagedPdf_IsAConversionError()
    {
        var input = Path.Combine(_dir, "bozuk.pdf");
        File.WriteAllText(input, "%PDF-1.4 not really");

        Assert.Throws<ConversionException>(() => PdfPageSelection.Apply(input, Path.Combine(_dir, "o.pdf"), "1", false));
        Assert.Throws<ConversionException>(() => PdfPageSelection.PageCount(input));
    }
}
