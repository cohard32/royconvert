using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RoyConvert.App.Platform;
using RoyConvert.App.Resources;
using RoyConvert.Core;

namespace RoyConvert.App.ViewModels;

/// <param name="Code">One of <see cref="AppSettings.Languages"/>, or null for the system language.</param>
public sealed record LanguageOption(string? Code, string Name);

public partial class HomeViewModel : ViewModelBase
{
    private readonly IReadOnlyList<Preset> _presets;
    private readonly Action<Preset, IReadOnlyList<string>> _startConversion;
    private readonly Action<string?> _changeLanguage;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConvertCommand))]
    private Preset? _selectedPreset;

    [ObservableProperty]
    private string _filesText = Strings.Home_NoFiles;

    [ObservableProperty]
    private string _shellMenuStatus = string.Empty;

    [ObservableProperty]
    private bool _isShellMenuRegistered;

    [ObservableProperty]
    private LanguageOption _selectedLanguage;

    public HomeViewModel(IReadOnlyList<Preset> presets, string? ffmpegPath, string? libreOfficePath, string? language,
        Action<Preset, IReadOnlyList<string>> startConversion, Action<string?> changeLanguage)
    {
        _presets = presets;
        _startConversion = startConversion;
        _changeLanguage = changeLanguage;
        FfmpegStatus = ffmpegPath is null ? Strings.Ffmpeg_NotFound : $"ffmpeg: {ffmpegPath}";
        LibreOfficeStatus = libreOfficePath is null ? Strings.LibreOffice_NotFound : $"LibreOffice: {libreOfficePath}";
        // Language names are written in their own language so they can be found whatever the current one is.
        Languages = [new(null, Strings.Language_System), new("en", "English"), new("tr", "Türkçe")];
        _selectedLanguage = Languages.FirstOrDefault(l => l.Code == language) ?? Languages[0];
        RefreshShellMenu();
    }

    public IReadOnlyList<string> Files { get; private set; } = [];

    public ObservableCollection<Preset> AvailablePresets { get; } = [];

    public IReadOnlyList<LanguageOption> Languages { get; }

    public string FfmpegStatus { get; }

    public string LibreOfficeStatus { get; }

    public bool CanManageShellMenu => OperatingSystem.IsWindows();

    public void SetFiles(IReadOnlyList<string> files)
    {
        Files = files;
        FilesText = files.Count switch
        {
            0 => Strings.Home_NoFilesShort,
            1 => files[0],
            _ => string.Format(Strings.Home_FilesSelected, files.Count),
        };

        AvailablePresets.Clear();
        foreach (var preset in PresetCatalog.ForFiles(_presets, files))
        {
            AvailablePresets.Add(preset);
        }

        SelectedPreset = AvailablePresets.FirstOrDefault();
        if (files.Count > 0 && AvailablePresets.Count == 0)
        {
            FilesText += " " + Strings.Home_NoCommonConversion;
        }
    }

    partial void OnSelectedLanguageChanged(LanguageOption value) => _changeLanguage(value.Code);

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
            var message = string.Format(Strings.Shell_Added, registration.Extensions);
            RefreshShellMenu(registration.ModernMenuError is { } error ? $"{message} {error}" : message);
        }
        catch (Exception e) when (e is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            RefreshShellMenu(string.Format(Strings.Shell_UpdateFailed, e.Message));
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
            RefreshShellMenu(error ?? Strings.Shell_Removed);
        }
        catch (Exception e) when (e is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            RefreshShellMenu(string.Format(Strings.Shell_UpdateFailed, e.Message));
        }
    }

    private void RefreshShellMenu(string? message = null)
    {
        IsShellMenuRegistered = OperatingSystem.IsWindows() && WindowsShellMenu.IsRegistered();
        ShellMenuStatus = message ?? (IsShellMenuRegistered ? Strings.Shell_Installed : Strings.Shell_NotInstalled);
    }
}
