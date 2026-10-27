using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using RoyConvert.App.Platform;
using RoyConvert.App.ViewModels;
using RoyConvert.App.Views;
using RoyConvert.Core;

namespace RoyConvert.App;

public partial class App : Application
{
    private readonly IReadOnlyList<Preset> _presets = PresetCatalog.Default;
    private readonly string? _ffmpegPath = ToolLocator.FindFfmpeg();
    private readonly string? _libreOfficePath = ToolLocator.FindLibreOffice();
    private readonly (IDocumentConverter? Converter, string? Apps) _microsoftOffice = FindMicrosoftOffice();

    /// <summary>Set by <see cref="Program"/> before the app starts.</summary>
    public CommandLine Command { get; init; } = CommandLine.Parse([]);

    /// <summary>Present when started for a conversion; receives files from later Explorer invocations.</summary>
    public SingleInstance? Instance { get; init; }

    /// <summary>Loaded by <see cref="Program"/>, which already applied the language.</summary>
    public AppSettings Settings { get; init; } = new();

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            if (Command.Kind == CommandKind.Home)
            {
                desktop.MainWindow = CreateMainWindow([]);
            }
            else
            {
                var progress = CreateProgress();
                progress.Add(Command);
                if (Instance is not null)
                {
                    Instance.Listen(progress.Receive);
                    desktop.Exit += (_, _) => Instance.Dispose();
                }

                desktop.MainWindow = new ProgressWindow(progress, Instance);
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    private MainWindow CreateMainWindow(IReadOnlyList<string> files)
    {
        var home = new HomeViewModel(_presets, _ffmpegPath, _libreOfficePath, _microsoftOffice.Apps, Settings.Language,
            ShowProgress, ChangeLanguage, () => new SettingsViewModel(Settings, () => Settings.Save()));
        if (files.Count > 0)
        {
            home.SetFiles(files);
        }

        return new MainWindow { DataContext = home };
    }

    private void ChangeLanguage(string? language)
    {
        if (language == Settings.Language)
        {
            return;
        }

        Settings.Language = language;
        Settings.ApplyLanguage();
        try
        {
            Settings.Save();
            if (OperatingSystem.IsWindows())
            {
                WindowsShellMenu.RefreshMenuFile(_presets);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // The new language still applies until the app closes.
        }

        // The texts come from {x:Static}, so a new window shows the new language. Posted because this runs while
        // the language list is still handling the selection.
        Dispatcher.UIThread.Post(() =>
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: MainWindow old } desktop)
            {
                var window = CreateMainWindow((old.DataContext as HomeViewModel)?.Files ?? []);
                window.Position = old.Position;
                window.WindowStartupLocation = Avalonia.Controls.WindowStartupLocation.Manual;
                desktop.MainWindow = window;
                window.Show();
                old.Close();
            }
        });
    }

    private void ShowProgress(Preset preset, IReadOnlyList<string> files)
    {
        var progress = CreateProgress();
        progress.Add(preset, files);
        new ProgressWindow(progress).Show();
    }

    private ProgressViewModel CreateProgress() =>
        new(ConversionRunner.CreateDefault(_ffmpegPath, Settings.MaxParallelism, _libreOfficePath, _microsoftOffice.Converter),
            _presets, Settings);

    /// <summary>Word, Excel and PowerPoint export Office documents best; only on Windows.</summary>
    private static (IDocumentConverter? Converter, string? Apps) FindMicrosoftOffice()
    {
        if (!OperatingSystem.IsWindows() || Core.Office.MicrosoftOfficeConverter.FindInstalled() is not { } office)
        {
            return (null, null);
        }

        return (office, string.Join(", ", office.Installed.Order()));
    }
}
