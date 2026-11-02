using Avalonia.Controls;
using Avalonia.Interactivity;
using RoyConvert.App.ViewModels;

namespace RoyConvert.App.Views;

public partial class ResizeWindow : Window
{
    public ResizeWindow()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is ResizeViewModel viewModel)
        {
            viewModel.CloseRequested += Close;
        }
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        WidthBox.Focus();
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        // The window's X while resizing: the picture being written is finished first; the window closes when all are done.
        if (DataContext is ResizeViewModel { IsBusy: true })
        {
            e.Cancel = true;
        }

        base.OnClosing(e);
    }
}
