using Avalonia.Controls;
using Avalonia.Threading;
using RoyConvert.App.ViewModels;

namespace RoyConvert.App.Views;

public partial class ProgressWindow : Window
{
    private static readonly TimeSpan CloseDelay = TimeSpan.FromSeconds(2);

    private readonly SingleInstance? _instance;

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
        viewModel.Completed += () => DispatcherTimer.RunOnce(CloseIfStillComplete, CloseDelay);
        Closed += (_, _) => viewModel.Dispose();
    }

    private void CloseIfStillComplete()
    {
        // Files may have arrived from Explorer during the delay. Once the instance stops accepting, later ones are
        // refused and their process opens its own window.
        if (DataContext is ProgressViewModel viewModel
            && (_instance?.TryStopAccepting(() => viewModel.IsComplete) ?? viewModel.IsComplete))
        {
            Close();
        }
    }
}
