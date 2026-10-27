using RoyConvert.App.ViewModels;
using RoyConvert.Core;

namespace RoyConvert.App.Tests;

[Collection(UiCultureCollection.Name)]
public sealed class SettingsTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"rc-settings-{Guid.NewGuid():N}", "settings.json");

    public void Dispose()
    {
        if (Directory.Exists(Path.GetDirectoryName(_path)))
        {
            Directory.Delete(Path.GetDirectoryName(_path)!, recursive: true);
        }
    }

    [Fact]
    public void ApplyTo_UsesTheUsersChoices()
    {
        var settings = new AppSettings { ImageQuality = 70, VideoQuality = 18, PdfDpi = 300 };

        var applied = settings.ApplyTo(PresetCatalog.Find(PresetCatalog.Default, "jpg")!.Settings);

        Assert.Equal(70, applied.ImageQuality);
        Assert.Equal(18, applied.VideoQuality);
        Assert.Equal(300, applied.PdfDpi);
    }

    [Fact]
    public void ApplyTo_KeepsAPresetsOwnQuality()
    {
        var small = PresetCatalog.Find(PresetCatalog.Default, "mp4-small")!.Settings;

        var applied = new AppSettings { VideoQuality = 18 }.ApplyTo(small);

        Assert.Equal(small.VideoQuality, applied.VideoQuality);
    }

    [Fact]
    public void ApplyTo_WithoutChoices_ChangesNothing()
    {
        var mp4 = PresetCatalog.Find(PresetCatalog.Default, "mp4")!.Settings;

        Assert.Equal(mp4, new AppSettings().ApplyTo(mp4));
    }

    [Fact]
    public void Load_BringsHandEditedValuesIntoRange()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, """{ "ImageQuality": 500, "PdfDpi": 1, "MaxParallelism": 0, "VideoQuality": 23 }""");

        var settings = AppSettings.Load(_path);

        Assert.Equal(100, settings.ImageQuality);
        Assert.Equal(36, settings.PdfDpi);
        Assert.Equal(1, settings.MaxParallelism);
        Assert.Equal(23, settings.VideoQuality);
    }

    [Fact]
    public void Save_StoresChangesAndLeavesDefaultsEmpty()
    {
        var settings = new AppSettings { Language = "tr" };
        var closed = false;
        var viewModel = new SettingsViewModel(settings, () => settings.Save(_path));
        viewModel.CloseRequested += () => closed = true;

        viewModel.ImageQuality = 75;
        viewModel.SelectedPdfDpi = viewModel.PdfDpis.Single(o => o.Value == 300);
        viewModel.SelectedParallelism = viewModel.Parallelisms.Single(o => o.Value == 1);
        viewModel.SaveCommand.Execute(null);

        Assert.True(closed);
        var loaded = AppSettings.Load(_path);
        Assert.Equal("tr", loaded.Language);
        Assert.Equal(75, loaded.ImageQuality);
        Assert.Equal(300, loaded.PdfDpi);
        Assert.Equal(1, loaded.MaxParallelism);
        Assert.Null(loaded.VideoQuality);
    }

    [Fact]
    public void Reset_ReturnsToTheDefaults()
    {
        var settings = new AppSettings { ImageQuality = 40, VideoQuality = 28, PdfDpi = 96, MaxParallelism = 2 };
        var viewModel = new SettingsViewModel(settings, () => settings.Save(_path));

        viewModel.ResetCommand.Execute(null);
        viewModel.SaveCommand.Execute(null);

        Assert.Null(settings.ImageQuality);
        Assert.Null(settings.VideoQuality);
        Assert.Null(settings.PdfDpi);
        Assert.Null(settings.MaxParallelism);
    }

    [Fact]
    public void HandEditedValue_IsShownAsAnOption()
    {
        var viewModel = new SettingsViewModel(new AppSettings { PdfDpi = 120 }, () => { });

        Assert.Equal(120, viewModel.SelectedPdfDpi.Value);
        Assert.Contains(viewModel.SelectedPdfDpi, viewModel.PdfDpis);
    }

    [Fact]
    public void FailedSave_KeepsTheWindowOpenWithAMessage()
    {
        var closed = false;
        var viewModel = new SettingsViewModel(new AppSettings(), () => throw new IOException("disk full"));
        viewModel.CloseRequested += () => closed = true;

        viewModel.SaveCommand.Execute(null);

        Assert.False(closed);
        Assert.Contains("disk full", viewModel.Status);
    }
}
