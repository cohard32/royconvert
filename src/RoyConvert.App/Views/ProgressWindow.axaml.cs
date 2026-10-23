using Avalonia.Controls;
using Avalonia.Threading;
using RoyConvert.App.ViewModels;

namespace RoyConvert.App.Views;

public partial class ProgressWindow : Window
{
    private static readonly TimeSpan CloseDelay = TimeSpan.FromSeconds(2);

    public ProgressWindow()
    {
        InitializeComponent();
    }

    public ProgressWindow(ProgressViewModel viewModel) : this()
    {
        DataContext = viewModel;
        viewModel.Completed += () => DispatcherTimer.RunOnce(CloseIfStillComplete, CloseDelay);
        Closed += (_, _) => viewModel.Dispose();
    }

    private void CloseIfStillComplete()
    {
        // Files may have arrived from Explorer during the delay.
        if (DataContext is ProgressViewModel { AllFinished: true } viewModel && viewModel.Jobs.All(j => j.IsDone))
        {
            Close();
        }
    }
}
