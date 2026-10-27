using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using RoyConvert.App.Resources;
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
            Title = Strings.Home_ChooseFilesTitle,
            AllowMultiple = true,
        });
        SetFiles(files);
    }

    private async void ChooseDvd_Click(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = Strings.Home_ChooseDvdTitle,
        });
        SetFiles(folders);
    }

    private async void Settings_Click(object? sender, RoutedEventArgs e)
    {
        if (ViewModel?.CreateSettings is { } create)
        {
            await new SettingsWindow { DataContext = create() }.ShowDialog(this);
        }
    }

    private void OnDrop(object? sender, DragEventArgs e) => SetFiles(e.DataTransfer.TryGetFiles() ?? []);

    /// <summary>Files, or folders and drives (a DVD is one).</summary>
    private void SetFiles(IEnumerable<IStorageItem> items)
    {
        var paths = items
            .Select(f => f.TryGetLocalPath())
            .OfType<string>()
            .ToList();
        if (paths.Count > 0)
        {
            ViewModel?.SetFiles(paths);
        }
    }
}
