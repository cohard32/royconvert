using Avalonia.Controls;
using Avalonia.Interactivity;
using RoyConvert.App.ViewModels;

namespace RoyConvert.App.Views;

public partial class TrimWindow : Window
{
    public TrimWindow()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is TrimViewModel viewModel)
        {
            viewModel.CloseRequested += Close;
        }
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        StartBox.Focus();
        StartBox.SelectAll();
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        // The window's X while cutting: stop first, so no half-written file is left; the window then closes itself.
        if (DataContext is TrimViewModel { IsBusy: true } viewModel)
        {
            e.Cancel = true;
            viewModel.CloseCommand.Execute(null);
        }

        base.OnClosing(e);
    }
}
