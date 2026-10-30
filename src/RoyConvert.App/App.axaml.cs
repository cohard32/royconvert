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
            else if (Command.Kind == CommandKind.Rename)
            {
                // Explorer sends all selected files at once, so each rename gets its own window.
                desktop.MainWindow = new RenameWindow { DataContext = new RenameViewModel(Command.Files) };
            }
            else if (Command.Kind == CommandKind.Watermark)
            {
                desktop.MainWindow = new WatermarkWindow { DataContext = new WatermarkViewModel(Command.Files) };
            }
            else if (Command.Kind == CommandKind.Trim)
            {
                desktop.MainWindow = new TrimWindow
                {
                    DataContext = new TrimViewModel(Command.Files, _ffmpegPath, Settings.ApplyTo(new ConversionSettings())),
                };
            }
            else if (Command.Kind is CommandKind.ProtectPdf or CommandKind.UnprotectPdf)
            {
                desktop.MainWindow = new PdfPasswordWindow
                {
                    DataContext = new PdfPasswordViewModel(Command.Files, protect: Command.Kind == CommandKind.ProtectPdf),
                };
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

            if (desktop.MainWindow is IUpdateHost host)
            {
                _ = CheckForUpdateAsync(host, desktop);
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>The running version (&lt;Version&gt; in the project file).</summary>
    public static Version Version { get; } = typeof(App).Assembly.GetName().Version is { } v
        ? new Version(v.Major, v.Minor, Math.Max(v.Build, 0))
        : new Version(0, 0, 0);

    /// <summary>
    /// At most about once a day, and only for an installed royconvert (a setup can only replace an install). A newer
    /// release shows a bar in <paramref name="host"/>; no network or a GitHub error is ignored until the next time.
    /// </summary>
    private async Task CheckForUpdateAsync(IUpdateHost host, IClassicDesktopStyleApplicationLifetime desktop)
    {
        if (!OperatingSystem.IsWindows() || !Settings.CheckForUpdates || !IsInstalled()
            || Settings.LastUpdateCheck > DateTimeOffset.Now.AddHours(-20))
        {
            return;
        }

        var http = Core.Updates.UpdateChecker.CreateClient(Version);
        Core.Updates.UpdateInfo? update;
        try
        {
            update = await Task.Run(() => Core.Updates.UpdateChecker.CheckAsync(http, Core.Updates.UpdateChecker.LatestReleaseUrl, Version));
            Settings.LastUpdateCheck = DateTimeOffset.Now;
            Settings.Save();
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException
                                      or IOException or UnauthorizedAccessException or InvalidOperationException
                                      or KeyNotFoundException or UriFormatException)
        {
            return;
        }

        if (update is null)
        {
            http.Dispose();
            return;
        }

        host.ShowUpdate(new UpdateViewModel(update, Version, http, () => host.CanInstallUpdate, setup =>
        {
            UpdateInstaller.Start(setup);
            desktop.Shutdown();
        }));
    }

    /// <summary>Installed by setup: its uninstaller is next to the exe (a build from source has none).</summary>
    private static bool IsInstalled() =>
        Environment.GetEnvironmentVariable("ROYCONVERT_UPDATE_URL") is { Length: > 0 }
        || Directory.EnumerateFiles(AppContext.BaseDirectory, "unins*.exe").Any();

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
