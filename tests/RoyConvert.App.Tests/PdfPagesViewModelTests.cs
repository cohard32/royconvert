using ImageMagick;
using RoyConvert.App.ViewModels;

namespace RoyConvert.App.Tests;

[Collection(UiCultureCollection.Name)]
public sealed class PdfPagesViewModelTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("royconvert-pages-vm-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string FivePages()
    {
        var path = Path.Combine(_dir, "belge.pdf");
        using var pages = new MagickImageCollection();
        for (var i = 0; i < 5; i++)
        {
            pages.Add(new MagickImage(MagickColors.White, 100, 140));
        }

        pages.Write(path, MagickFormat.Pdf);
        return path;
    }

    [Fact]
    public void JustOpened_ShowsThePageCount_NoErrorYet()
    {
        var vm = new PdfPagesViewModel([FivePages()]);

        Assert.Contains("5", vm.Preview);
        Assert.Empty(vm.Problem);
        Assert.False(vm.ApplyCommand.CanExecute(null));
        Assert.True(vm.Keep);
    }

    [Fact]
    public void Pages_ShowWhatIsLeft_AndProblems()
    {
        var vm = new PdfPagesViewModel([FivePages()]) { Pages = "1-2" };
        Assert.Contains("5", vm.Preview);
        Assert.Contains("→ 2 ", vm.Preview);
        Assert.True(vm.ApplyCommand.CanExecute(null));

        vm.Delete = true;
        Assert.False(vm.Keep);
        Assert.Contains("→ 3 ", vm.Preview);

        vm.Pages = "1-";
        Assert.NotEmpty(vm.Problem);
        Assert.False(vm.ApplyCommand.CanExecute(null));

        vm.Pages = "7";
        Assert.Contains("7", vm.Problem);
        Assert.False(vm.ApplyCommand.CanExecute(null));
    }

    [Fact]
    public void OnlyPdfs_AreTaken()
    {
        var text = Path.Combine(_dir, "a.txt");
        File.WriteAllText(text, "x");

        var vm = new PdfPagesViewModel([text]);

        Assert.NotEmpty(vm.Status);
        Assert.Empty(vm.Preview);
        Assert.False(vm.ApplyCommand.CanExecute(null));
    }
}
