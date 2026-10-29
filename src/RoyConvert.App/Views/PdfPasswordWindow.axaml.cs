using Avalonia.Controls;
using Avalonia.Interactivity;
using RoyConvert.App.ViewModels;

namespace RoyConvert.App.Views;

public partial class PdfPasswordWindow : Window
{
    public PdfPasswordWindow()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is PdfPasswordViewModel viewModel)
        {
            viewModel.CloseRequested += Close;
        }
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        PasswordBox.Focus();
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        // A PDF is being written; closing now would leave it half done.
        if (DataContext is PdfPasswordViewModel { IsBusy: true })
        {
            e.Cancel = true;
        }

        base.OnClosing(e);
    }
}
