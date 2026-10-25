using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RoyConvert.App.Platform;
using RoyConvert.Core;

namespace RoyConvert.App.ViewModels;

public partial class HomeViewModel : ViewModelBase
{
    private readonly IReadOnlyList<Preset> _presets;
    private readonly Action<Preset, IReadOnlyList<string>> _startConversion;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConvertCommand))]
    private Preset? _selectedPreset;

    [ObservableProperty]
    private string _filesText = "No files selected. Choose files or drop them here.";

    [ObservableProperty]
    private string _shellMenuStatus = string.Empty;

    [ObservableProperty]
    private bool _isShellMenuRegistered;

    public HomeViewModel(IReadOnlyList<Preset> presets, string? ffmpegPath, Action<Preset, IReadOnlyList<string>> startConversion)
    {
        _presets = presets;
        _startConversion = startConversion;
        FfmpegStatus = ffmpegPath is null
            ? "ffmpeg not found: video and audio conversions are unavailable."
            : $"ffmpeg: {ffmpegPath}";
        RefreshShellMenu();
    }

    public IReadOnlyList<string> Files { get; private set; } = [];

    public ObservableCollection<Preset> AvailablePresets { get; } = [];

    public string FfmpegStatus { get; }

    public bool CanManageShellMenu => OperatingSystem.IsWindows();

    public void SetFiles(IReadOnlyList<string> files)
    {
        Files = files;
        FilesText = files.Count switch
        {
            0 => "No files selected.",
            1 => files[0],
            _ => $"{files.Count} files selected",
        };

        AvailablePresets.Clear();
        foreach (var preset in PresetCatalog.ForFiles(_presets, files))
        {
            AvailablePresets.Add(preset);
        }

        SelectedPreset = AvailablePresets.FirstOrDefault();
        if (files.Count > 0 && AvailablePresets.Count == 0)
        {
            FilesText += " (no common conversion for these files)";
        }
    }

    private bool CanConvert() => SelectedPreset is not null && Files.Count > 0;

    [RelayCommand(CanExecute = nameof(CanConvert))]
    private void Convert() => _startConversion(SelectedPreset!, Files);

    [RelayCommand]
    private async Task RegisterShellMenu()
    {
        if (!OperatingSystem.IsWindows() || Environment.ProcessPath is not { } exe)
        {
            return;
        }

        try
        {
            // The Windows 11 package is installed through PowerShell, which takes a few seconds.
            // The platform check is repeated because the analyzer does not carry it into the lambda.
            var registration = await Task.Run(() => OperatingSystem.IsWindows()
                ? WindowsShellMenu.Register(exe, _presets)
                : throw new PlatformNotSupportedException());
            var message = $"Added to the right-click menu for {registration.Extensions} file types.";
            RefreshShellMenu(registration.ModernMenuError is { } error ? $"{message} {error}" : message);
        }
        catch (Exception e) when (e is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            RefreshShellMenu($"Could not update the right-click menu: {e.Message}");
        }
    }

    [RelayCommand]
    private async Task UnregisterShellMenu()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            var error = await Task.Run(WindowsShellMenu.Unregister);
            RefreshShellMenu(error ?? "Removed from the right-click menu.");
        }
        catch (Exception e) when (e is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            RefreshShellMenu($"Could not update the right-click menu: {e.Message}");
        }
    }

    private void RefreshShellMenu(string? message = null)
    {
        IsShellMenuRegistered = OperatingSystem.IsWindows() && WindowsShellMenu.IsRegistered();
        ShellMenuStatus = message ?? (IsShellMenuRegistered
            ? "The right-click menu is installed."
            : "The right-click menu is not installed.");
    }
}
