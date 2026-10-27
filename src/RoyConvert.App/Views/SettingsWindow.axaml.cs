using Avalonia.Controls;
using RoyConvert.App.ViewModels;

namespace RoyConvert.App.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is SettingsViewModel viewModel)
        {
            viewModel.CloseRequested += Close;
        }
    }
}
