using RoyConvert.Core.Imaging;

namespace RoyConvert.Core.Tests;

public sealed class PagePathsTests
{
    private static readonly string Output = Path.Combine("C:", "docs", "report.png");

    [Fact]
    public void SinglePage_UsesTheOutput()
    {
        Assert.Equal([Output], ImageConverter.PagePaths(Output, 1, _ => false));
    }

    [Fact]
    public void Pages_AreNumberedWithAtLeastTwoDigits()
    {
        var paths = ImageConverter.PagePaths(Output, 3, _ => false).Select(Path.GetFileName);

        Assert.Equal(["report-01.png", "report-02.png", "report-03.png"], paths);
    }

    [Fact]
    public void Numbers_GrowWithThePageCount()
    {
        var paths = ImageConverter.PagePaths(Output, 120, _ => false);

        Assert.Equal("report-001.png", Path.GetFileName(paths[0]));
        Assert.Equal("report-120.png", Path.GetFileName(paths[^1]));
    }

    [Fact]
    public void ExistingFiles_AreNotOverwritten()
    {
        var taken = Path.Combine("C:", "docs", "report-02.png");

        var paths = ImageConverter.PagePaths(Output, 2, path => path == taken).Select(Path.GetFileName);

        Assert.Equal(["report-01.png", "report-02 (1).png"], paths);
    }
}
