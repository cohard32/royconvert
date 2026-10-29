using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RoyConvert.App.Resources;
using RoyConvert.Core;

namespace RoyConvert.App.ViewModels;

/// <param name="Value">The stored value; null for the built-in default.</param>
public sealed record SettingOption(int? Value, string Name);

/// <summary>Edits the conversion choices of <see cref="AppSettings"/>; nothing changes until <see cref="SaveCommand"/>.</summary>
public partial class SettingsViewModel : ViewModelBase
{
    private static readonly ConversionSettings Defaults = new();

    private readonly AppSettings _settings;
    private readonly Action _save;

    [ObservableProperty]
    private double _imageQuality;

    [ObservableProperty]
    private SettingOption _selectedVideoQuality;

    [ObservableProperty]
    private SettingOption _selectedPdfDpi;

    [ObservableProperty]
    private SettingOption _selectedParallelism;

    [ObservableProperty]
    private bool _checkForUpdates;

    [ObservableProperty]
    private string _status = string.Empty;

    /// <param name="save">Writes <paramref name="settings"/> to disk; may throw <see cref="IOException"/>.</param>
    public SettingsViewModel(AppSettings settings, Action save)
    {
        _settings = settings;
        _save = save;
        (VideoQualities, _selectedVideoQuality) = WithSelected(
        [
            new(18, Strings.Settings_VideoHigh),
            new(Defaults.VideoQuality, Strings.Settings_VideoNormal),
            new(28, Strings.Settings_VideoSmall),
        ], settings.VideoQuality ?? Defaults.VideoQuality, value => $"CRF {value}");
        (PdfDpis, _selectedPdfDpi) = WithSelected(
            [new(96, "96"), new(Defaults.PdfDpi, "150"), new(200, "200"), new(300, "300")],
            settings.PdfDpi ?? Defaults.PdfDpi, value => value.ToString());
        Parallelisms =
        [
            new(null, string.Format(Strings.Settings_Automatic, ConversionRunner.DefaultParallelism)),
            .. Enumerable.Range(1, Environment.ProcessorCount).Select(n => new SettingOption(n, n.ToString())),
        ];

        _imageQuality = settings.ImageQuality ?? Defaults.ImageQuality;
        _selectedParallelism = Parallelisms.FirstOrDefault(o => o.Value == settings.MaxParallelism) ?? Parallelisms[0];
        _checkForUpdates = settings.CheckForUpdates;
    }

    public IReadOnlyList<SettingOption> VideoQualities { get; }

    public IReadOnlyList<SettingOption> PdfDpis { get; }

    public IReadOnlyList<SettingOption> Parallelisms { get; }

    /// <summary>Raised after a successful save, or on cancel, so the window can close.</summary>
    public event Action? CloseRequested;

    [RelayCommand]
    private void Save()
    {
        _settings.ImageQuality = OrNull((int)Math.Round(ImageQuality), Defaults.ImageQuality);
        _settings.VideoQuality = OrNull(SelectedVideoQuality.Value, Defaults.VideoQuality);
        _settings.PdfDpi = OrNull(SelectedPdfDpi.Value, Defaults.PdfDpi);
        _settings.MaxParallelism = SelectedParallelism.Value;
        _settings.CheckForUpdates = CheckForUpdates;
        try
        {
            _save();
            CloseRequested?.Invoke();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // The new values still apply until the app closes.
            Status = string.Format(Strings.Settings_SaveFailed, e.Message);
        }
    }

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke();

    [RelayCommand]
    private void Reset()
    {
        ImageQuality = Defaults.ImageQuality;
        SelectedVideoQuality = VideoQualities.First(o => o.Value == Defaults.VideoQuality);
        SelectedPdfDpi = PdfDpis.First(o => o.Value == Defaults.PdfDpi);
        SelectedParallelism = Parallelisms[0];
        CheckForUpdates = true;
    }

    /// <summary>Defaults are stored as null, so a later change of a built-in default reaches the user.</summary>
    private static int? OrNull(int? value, int builtIn) => value == builtIn ? null : value;

    /// <summary>The options and the one with <paramref name="value"/>; a value edited by hand in settings.json is added.</summary>
    private static (IReadOnlyList<SettingOption> Options, SettingOption Selected) WithSelected(
        IReadOnlyList<SettingOption> options, int value, Func<int, string> name)
    {
        if (options.FirstOrDefault(o => o.Value == value) is { } found)
        {
            return (options, found);
        }

        var custom = new SettingOption(value, name(value));
        return ([.. options, custom], custom);
    }
}
