using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ImageMagick;
using RoyConvert.App.Resources;
using RoyConvert.Core;

namespace RoyConvert.App.ViewModels;

public sealed record PositionOption(WatermarkPosition Value, string Name);

/// <summary>Writes a watermarked copy of each picture and PDF, with a preview of the first one.</summary>
public partial class WatermarkViewModel : ViewModelBase
{
    private const uint PreviewSize = 560;

    private readonly IReadOnlyList<string> _files;
    private readonly MagickImage? _previewBase;
    private int _previewVersion;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    private string _text = string.Empty;

    [ObservableProperty]
    private PositionOption _position;

    /// <summary>Percent.</summary>
    [ObservableProperty]
    private double _opacity = 40;

    /// <summary>Text height in percent of the shorter side.</summary>
    [ObservableProperty]
    private double _size = 8;

    [ObservableProperty]
    private Bitmap? _preview;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private string _status = string.Empty;

    [ObservableProperty]
    private string _errors = string.Empty;

    public WatermarkViewModel(IEnumerable<string> files)
    {
        _files = files.Where(f => File.Exists(f) && Watermark.Inputs.Contains(Formats.ExtensionOf(f))).ToList();
        Title = string.Format(Strings.Watermark_Title, _files.Count);
        Positions =
        [
            new(WatermarkPosition.Diagonal, Strings.Watermark_Diagonal),
            new(WatermarkPosition.BottomRight, Strings.Watermark_BottomRight),
            new(WatermarkPosition.Tiled, Strings.Watermark_Tiled),
        ];
        _position = Positions[0];
        if (_files.Count == 0)
        {
            Status = Strings.Watermark_NoFiles;
            return;
        }

        try
        {
            _previewBase = Watermark.LoadPreview(_files[0], PreviewSize);
        }
        catch (ConversionException e)
        {
            // The real run reports this file again; the others may still work.
            Errors = $"{Path.GetFileName(_files[0])}: {e.Message}";
        }

        UpdatePreview();
    }

    /// <summary>Raised when every file is done, or on close.</summary>
    public event Action? CloseRequested;

    public string Title { get; }

    public IReadOnlyList<PositionOption> Positions { get; }

    public string PreviewName => _files.Count > 0 ? Path.GetFileName(_files[0]) : string.Empty;

    private WatermarkOptions Options => new(Text.Trim(), Position.Value, Opacity / 100, Size / 100);

    partial void OnTextChanged(string value) => UpdatePreview();

    partial void OnPositionChanged(PositionOption value) => UpdatePreview();

    partial void OnOpacityChanged(double value) => UpdatePreview();

    partial void OnSizeChanged(double value) => UpdatePreview();

    /// <summary>Redraws in the background; a newer change while typing wins over an older one.</summary>
    private void UpdatePreview()
    {
        if (_previewBase is null)
        {
            return;
        }

        var version = ++_previewVersion;
        var options = Options;
        Task.Run(async () =>
        {
            await Task.Delay(120);
            if (version != _previewVersion)
            {
                return;
            }

            byte[] png;
            lock (_previewBase)
            {
                using var image = (MagickImage)_previewBase.Clone();
                Watermark.Draw(image, options);
                png = image.ToByteArray(MagickFormat.Png);
            }

            Dispatcher.UIThread.Post(() =>
            {
                if (version == _previewVersion)
                {
                    using var stream = new MemoryStream(png);
                    Preview = new Bitmap(stream);
                }
            });
        });
    }

    private bool CanApply() => !IsBusy && _files.Count > 0 && !string.IsNullOrWhiteSpace(Text);

    [RelayCommand(CanExecute = nameof(CanApply))]
    private async Task ApplyAsync()
    {
        IsBusy = true;
        Errors = string.Empty;
        var options = Options;
        var suffix = Strings.Watermark_FileSuffix;
        var failed = new List<string>();
        for (var i = 0; i < _files.Count; i++)
        {
            Status = string.Format(Strings.Watermark_Progress, i + 1, _files.Count);
            var input = _files[i];
            try
            {
                await Task.Run(() => Watermark.Apply(input, Watermark.OutputPathFor(input, suffix), options));
            }
            catch (ConversionException e)
            {
                failed.Add($"{Path.GetFileName(input)}: {e.Message}");
            }
        }

        IsBusy = false;
        if (failed.Count == 0)
        {
            CloseRequested?.Invoke();
            return;
        }

        Status = string.Format(Strings.Watermark_Failed, failed.Count, _files.Count);
        Errors = string.Join(Environment.NewLine, failed);
    }

    [RelayCommand]
    private void Close() => CloseRequested?.Invoke();
}
