using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using RoyConvert.App.ViewModels;
using RoyConvert.App.Views;
using RoyConvert.Core;

namespace RoyConvert.App;

public partial class App : Application
{
    private readonly IReadOnlyList<Preset> _presets = PresetCatalog.Default;
    private readonly string? _ffmpegPath = ToolLocator.FindFfmpeg();

    /// <summary>Set by <see cref="Program"/> before the app starts.</summary>
    public CommandLine Command { get; init; } = CommandLine.Parse([]);

    /// <summary>Present when started for a conversion; receives files from later Explorer invocations.</summary>
    public SingleInstance? Instance { get; init; }

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
                desktop.MainWindow = new MainWindow
                {
                    DataContext = new HomeViewModel(_presets, _ffmpegPath, ShowProgress),
                };
            }
            else
            {
                var progress = CreateProgress();
                progress.Add(Command);
                if (Instance is not null)
                {
                    Instance.Listen(args => Dispatcher.UIThread.Post(() => progress.Add(CommandLine.Parse(args))));
                    desktop.Exit += (_, _) => Instance.Dispose();
                }

                desktop.MainWindow = new ProgressWindow(progress);
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void ShowProgress(Preset preset, IReadOnlyList<string> files)
    {
        var progress = CreateProgress();
        progress.Add(preset, files);
        new ProgressWindow(progress).Show();
    }

    private ProgressViewModel CreateProgress() =>
        new(ConversionRunner.CreateDefault(_ffmpegPath), _presets);
}
