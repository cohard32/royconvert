using Avalonia.Controls;
using Avalonia.Interactivity;

namespace RoyConvert.App.Views;

public partial class CombineOrderWindow : Window
{
    public CombineOrderWindow()
    {
        InitializeComponent();
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        FileList.Focus();
    }
}
