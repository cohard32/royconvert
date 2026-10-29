using RoyConvert.App.Platform;
using RoyConvert.App.ViewModels;

namespace RoyConvert.App.Tests;

[Collection(UiCultureCollection.Name)]
public sealed class UpdateTests : IDisposable
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
    public void Setup_InProgramFiles_IsForAllUsers_ElsewhereForThisUser()
    {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        Assert.Equal(["/SILENT", "/SUPPRESSMSGBOXES", "/NORESTART", "/ALLUSERS"],
            UpdateInstaller.Arguments(Path.Combine(programFiles, "royconvert") + Path.DirectorySeparatorChar));
        Assert.Equal("/CURRENTUSER", UpdateInstaller.Arguments(Path.Combine(local, "Programs", "royconvert"))[^1]);
        // A folder that only starts like Program Files is not in it.
        Assert.Equal("/CURRENTUSER", UpdateInstaller.Arguments(programFiles + "X\\royconvert")[^1]);
    }

    [Fact]
    public void CheckForUpdates_IsOnByDefault_AndSaved()
    {
        Assert.True(AppSettings.Load(_path).CheckForUpdates);

        var checkedAt = new DateTimeOffset(2026, 10, 29, 22, 0, 0, TimeSpan.FromHours(3));
        new AppSettings { CheckForUpdates = false, LastUpdateCheck = checkedAt }.Save(_path);

        var loaded = AppSettings.Load(_path);
        Assert.False(loaded.CheckForUpdates);
        Assert.Equal(checkedAt, loaded.LastUpdateCheck);
    }

    [Fact]
    public void SettingsWindow_SavesTheUpdateChoice_AndResetTurnsItBackOn()
    {
        var settings = new AppSettings();
        var window = new SettingsViewModel(settings, () => settings.Save(_path)) { CheckForUpdates = false };

        window.SaveCommand.Execute(null);
        Assert.False(settings.CheckForUpdates);

        window.ResetCommand.Execute(null);
        Assert.True(window.CheckForUpdates);
    }

    [Fact]
    public void Version_ComesFromTheProject()
    {
        Assert.True(App.Version > new Version(0, 0, 0));
        Assert.Equal(3, App.Version.ToString().Split('.').Length);
    }
}
