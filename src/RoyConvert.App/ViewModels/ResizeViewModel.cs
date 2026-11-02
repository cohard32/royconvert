using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RoyConvert.App.Resources;
using RoyConvert.Core;

namespace RoyConvert.App.ViewModels;

/// <summary>Makes each selected picture smaller into a new file next to it.</summary>
public partial class ResizeViewModel : ViewModelBase
{
    private readonly IReadOnlyList<string> _files;
    private readonly int _quality;
    private readonly (uint Width, uint Height)? _firstSize;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    [NotifyPropertyChangedFor(nameof(Problem), nameof(Preview))]
    private bool _byPercent;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    [NotifyPropertyChangedFor(nameof(Problem), nameof(Preview))]
    private string _width = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    [NotifyPropertyChangedFor(nameof(Problem), nameof(Preview))]
    private string _height = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    [NotifyPropertyChangedFor(nameof(Problem), nameof(Preview))]
    private string _percent = "50";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    private bool _isBusy;

    /// <summary>0..1 over all files.</summary>
    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private string _status = string.Empty;

    [ObservableProperty]
    private string _errors = string.Empty;

    /// <param name="quality">For pictures written as JPEG instead of their own format (HEIC, camera raw…).</param>
    public ResizeViewModel(IEnumerable<string> files, int quality)
    {
        _files = files.Where(f => File.Exists(f) && ImageResize.Inputs.Contains(Formats.ExtensionOf(f))).ToList();
        _quality = quality;
        Title = string.Format(Strings.Resize_Title, _files.Count);
        if (_files.Count == 0)
        {
            Status = Strings.Resize_NoFiles;
            return;
        }

        try
        {
            _firstSize = ImageResize.ReadSize(_files[0]);
        }
        catch (ConversionException)
        {
            // Shown as unknown; resizing reports the real problem.
        }
    }

    /// <summary>Raised when every file is done, or on close.</summary>
    public event Action? CloseRequested;

    public string Title { get; }

    /// <summary>Two-way with the "Pixels" choice, the other side of <see cref="ByPercent"/>.</summary>
    public bool ByPixels
    {
        get => !ByPercent;
        set => ByPercent = !value;
    }

    partial void OnByPercentChanged(bool value) => OnPropertyChanged(nameof(ByPixels));

    /// <summary>
    /// What is wrong with the size as written; empty when it can be used, and while both boxes are still empty (the
    /// window has just opened: the button is off, there is nothing to correct yet).
    /// </summary>
    public string Problem =>
        Options() is not null || (!ByPercent && string.IsNullOrWhiteSpace(Width) && string.IsNullOrWhiteSpace(Height))
            ? string.Empty
            : ByPercent ? Strings.Resize_BadPercent : Strings.Resize_NeedSize;

    /// <summary>The first picture's size now and after resizing.</summary>
    public string Preview
    {
        get
        {
            if (_files.Count == 0)
            {
                return string.Empty;
            }

            var name = Path.GetFileName(_files[0]);
            if (_firstSize is not { } size)
            {
                return string.Format(Strings.Resize_PreviewUnknown, name);
            }

            var (w, h) = Options() is { } options ? ImageResize.TargetSize(size.Width, size.Height, options) : size;
            return string.Format(Strings.Resize_Preview, name, size.Width, size.Height, w, h);
        }
    }

    /// <summary>The size as written, or null if it cannot be used (a box left empty is "to match").</summary>
    public ResizeOptions? Options()
    {
        if (ByPercent)
        {
            return ImageResize.ParsePercent(Percent) is { } percent ? new ResizeOptions(Percent: percent) : null;
        }

        var width = string.IsNullOrWhiteSpace(Width) ? null : ImageResize.ParsePixels(Width);
        var height = string.IsNullOrWhiteSpace(Height) ? null : ImageResize.ParsePixels(Height);
        var bad = (!string.IsNullOrWhiteSpace(Width) && width is null) || (!string.IsNullOrWhiteSpace(Height) && height is null);
        return bad || (width is null && height is null) ? null : new ResizeOptions(width, height);
    }

    private bool CanApply() => !IsBusy && _files.Count > 0 && Options() is not null;

    [RelayCommand(CanExecute = nameof(CanApply))]
    private async Task ApplyAsync()
    {
        var options = Options()!;
        var suffix = Strings.Resize_FileSuffix;
        IsBusy = true;
        Errors = string.Empty;
        Progress = 0;
        var failed = new List<string>();
        for (var i = 0; i < _files.Count; i++)
        {
            Status = string.Format(Strings.Watermark_Progress, i + 1, _files.Count);
            var input = _files[i];
            try
            {
                await Task.Run(() => ImageResize.Apply(input, ImageResize.OutputPathFor(input, suffix), options, _quality));
            }
            catch (ConversionException e)
            {
                failed.Add($"{Path.GetFileName(input)}: {e.Message}");
            }

            Progress = (i + 1.0) / _files.Count;
        }

        IsBusy = false;
        if (failed.Count == 0)
        {
            CloseRequested?.Invoke();
            return;
        }

        Status = string.Format(Strings.Resize_Failed, failed.Count, _files.Count);
        Errors = string.Join(Environment.NewLine, failed);
    }

    [RelayCommand]
    private void Close() => CloseRequested?.Invoke();
}
