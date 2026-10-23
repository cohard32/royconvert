using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using RoyConvert.App.ViewModels;

namespace RoyConvert.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        AddHandler(DragDrop.DropEvent, OnDrop);
    }

    private HomeViewModel? ViewModel => DataContext as HomeViewModel;

    private async void ChooseFiles_Click(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose files to convert",
            AllowMultiple = true,
        });
        SetFiles(files);
    }

    private void OnDrop(object? sender, DragEventArgs e) => SetFiles(e.DataTransfer.TryGetFiles() ?? []);

    private void SetFiles(IEnumerable<IStorageItem> items)
    {
        var paths = items.OfType<IStorageFile>()
            .Select(f => f.TryGetLocalPath())
            .OfType<string>()
            .ToList();
        if (paths.Count > 0)
        {
            ViewModel?.SetFiles(paths);
        }
    }
}
