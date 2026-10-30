using Avalonia.Controls;
using Avalonia.Threading;
using RoyConvert.App.ViewModels;

namespace RoyConvert.App.Views;

public partial class ProgressWindow : Window, IUpdateHost
{
    private static readonly TimeSpan CloseDelay = TimeSpan.FromSeconds(2);

    private readonly SingleInstance? _instance;
    private UpdateViewModel? _update;

    public ProgressWindow()
    {
        InitializeComponent();
    }

    /// <param name="instance">
    /// Receives files from later Explorer invocations; it stops accepting them before the window closes.
    /// </param>
    public ProgressWindow(ProgressViewModel viewModel, SingleInstance? instance = null) : this()
    {
        DataContext = viewModel;
        _instance = instance;
        viewModel.Completed += () =>
        {
            _update?.Refresh();
            DispatcherTimer.RunOnce(CloseIfStillComplete, CloseDelay);
        };
        Closed += (_, _) => viewModel.Dispose();
    }

    /// <summary>Setup replaces the files that convert, so it waits until every job is done.</summary>
    public bool CanInstallUpdate => DataContext is ProgressViewModel { IsComplete: true };

    /// <summary>The window then stays open when the files are done, until the bar is answered.</summary>
    public void ShowUpdate(UpdateViewModel update)
    {
        _update = update;
        UpdateHost.Content = update;
        update.Dismissed += CloseIfStillComplete;
    }

    private void CloseIfStillComplete()
    {
        if (_update is { IsVisible: true })
        {
            return;
        }

        // Files may have arrived from Explorer during the delay. Once the instance stops accepting, later ones are
        // refused and their process opens its own window.
        if (DataContext is ProgressViewModel viewModel
            && (_instance?.TryStopAccepting(() => viewModel.IsComplete) ?? viewModel.IsComplete))
        {
            Close();
        }
    }
}
