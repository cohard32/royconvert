using Avalonia.Controls;
using Avalonia.Interactivity;
using RoyConvert.App.ViewModels;

namespace RoyConvert.App.Views;

public partial class WatermarkWindow : Window
{
    public WatermarkWindow()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is WatermarkViewModel viewModel)
        {
            viewModel.CloseRequested += Close;
        }
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        TextBox.Focus();
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        // Files are still being written; closing now would leave one half done.
        if (DataContext is WatermarkViewModel { IsBusy: true })
        {
            e.Cancel = true;
        }

        base.OnClosing(e);
    }
}
