using ImageMagick;
using RoyConvert.App.ViewModels;

namespace RoyConvert.App.Tests;

[Collection(UiCultureCollection.Name)]
public sealed class ResizeViewModelTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("royconvert-resize-vm-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string Picture()
    {
        var path = Path.Combine(_dir, "resim.png");
        using var image = new MagickImage(MagickColors.Orange, 1200, 800);
        image.Write(path);
        return path;
    }

    [Fact]
    public void JustOpened_NoErrorYet_ButNothingToApply()
    {
        var vm = new ResizeViewModel([Picture()], quality: 90);

        Assert.Empty(vm.Problem);
        Assert.False(vm.ApplyCommand.CanExecute(null));
        Assert.Contains("1200 × 800 → 1200 × 800", vm.Preview);
    }

    [Fact]
    public void Width_ShowsTheNewSize_BadTextShowsAProblem()
    {
        var vm = new ResizeViewModel([Picture()], quality: 90) { Width = "600" };
        Assert.Empty(vm.Problem);
        Assert.True(vm.ApplyCommand.CanExecute(null));
        Assert.Contains("→ 600 × 400", vm.Preview);

        vm.Height = "yarım";
        Assert.NotEmpty(vm.Problem);
        Assert.False(vm.ApplyCommand.CanExecute(null));
    }

    [Fact]
    public void Percent_IsTheOtherChoice()
    {
        var vm = new ResizeViewModel([Picture()], quality: 90) { ByPercent = true };
        Assert.False(vm.ByPixels);
        Assert.Contains("→ 600 × 400", vm.Preview); // 50 % by default

        vm.Percent = "100";
        Assert.NotEmpty(vm.Problem);
        Assert.False(vm.ApplyCommand.CanExecute(null));
    }

    [Fact]
    public void OnlyPicturesItCanResize_AreTaken()
    {
        var text = Path.Combine(_dir, "not.txt");
        File.WriteAllText(text, "x");

        var vm = new ResizeViewModel([text, Path.Combine(_dir, "yok.jpg")], quality: 90);

        Assert.NotEmpty(vm.Status);
        Assert.Empty(vm.Preview);
        Assert.False(vm.ApplyCommand.CanExecute(null));
    }
}
