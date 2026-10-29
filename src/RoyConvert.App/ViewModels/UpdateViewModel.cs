using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RoyConvert.App.Resources;
using RoyConvert.Core.Updates;

namespace RoyConvert.App.ViewModels;

/// <summary>A newer release: shown as a bar in a window, with "Update" (download, check, run setup) and "Later".</summary>
public partial class UpdateViewModel : ViewModelBase
{
    private readonly UpdateInfo _update;
    private readonly HttpClient _http;
    private readonly Func<bool> _canInstall;
    private readonly Action<string> _install;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(InstallCommand))]
    private bool _isDownloading;

    /// <summary>0..1 while downloading.</summary>
    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private string _status;

    [ObservableProperty]
    private bool _isVisible = true;

    /// <param name="canInstall">False while setup must wait, e.g. files are still being converted.</param>
    /// <param name="install">Starts the downloaded setup and closes royconvert.</param>
    public UpdateViewModel(UpdateInfo update, Version current, HttpClient http, Func<bool> canInstall, Action<string> install)
    {
        _update = update;
        _http = http;
        _canInstall = canInstall;
        _install = install;
        _status = string.Format(Strings.Update_Available, update.Version.ToString(3), current.ToString(3));
    }

    /// <summary>"Later" was chosen.</summary>
    public event Action? Dismissed;

    /// <summary>Call when what <c>canInstall</c> depends on has changed.</summary>
    public void Refresh() => InstallCommand.NotifyCanExecuteChanged();

    private bool CanInstall() => !IsDownloading && _canInstall();

    [RelayCommand(CanExecute = nameof(CanInstall))]
    private async Task InstallAsync()
    {
        IsDownloading = true;
        var progress = new Progress<double>(p =>
        {
            Progress = p;
            Status = string.Format(Strings.Update_Downloading, p * 100);
        });
        try
        {
            var folder = Path.Combine(Path.GetTempPath(), "royconvert-update");
            var setup = await Task.Run(() => UpdateChecker.DownloadAsync(_http, _update, folder, progress));
            _install(setup);
        }
        catch (Exception e) when (e is HttpRequestException or IOException or InvalidDataException or TaskCanceledException
                                      or System.ComponentModel.Win32Exception)
        {
            Status = string.Format(Strings.Update_Failed, e is InvalidDataException ? Strings.Update_BadFile : e.Message);
            IsDownloading = false;
        }
    }

    [RelayCommand]
    private void Later()
    {
        IsVisible = false;
        Dispatcher.UIThread.Post(() => Dismissed?.Invoke());
    }
}
