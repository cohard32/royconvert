using Avalonia.Controls;
using Avalonia.Interactivity;
using RoyConvert.App.ViewModels;

namespace RoyConvert.App.Views;

public partial class PdfPagesWindow : Window
{
    public PdfPagesWindow()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is PdfPagesViewModel viewModel)
        {
            viewModel.CloseRequested += Close;
        }
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        PagesBox.Focus();
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        // The window's X while saving: the PDF being written is finished first; the window closes when all are done.
        if (DataContext is PdfPagesViewModel { IsBusy: true })
        {
            e.Cancel = true;
        }

        base.OnClosing(e);
    }
}
