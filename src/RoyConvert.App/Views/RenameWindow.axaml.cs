using Avalonia.Controls;
using Avalonia.Interactivity;
using RoyConvert.App.ViewModels;

namespace RoyConvert.App.Views;

public partial class RenameWindow : Window
{
    public RenameWindow()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is RenameViewModel viewModel)
        {
            viewModel.CloseRequested += Close;
        }
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        // Ready to type over the suggested name.
        PatternBox.Focus();
        PatternBox.SelectAll();
    }
}
