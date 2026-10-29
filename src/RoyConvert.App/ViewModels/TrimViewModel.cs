using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RoyConvert.App.Resources;
using RoyConvert.Core;
using RoyConvert.Core.Ffmpeg;

namespace RoyConvert.App.ViewModels;

/// <summary>Cuts the same part out of each selected video or sound file into a new file next to it.</summary>
public partial class TrimViewModel : ViewModelBase
{
    private readonly IReadOnlyList<string> _files;
    private readonly FfmpegConverter? _ffmpeg;
    private readonly ConversionSettings _settings;
    private CancellationTokenSource? _cancel;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    [NotifyPropertyChangedFor(nameof(Problem))]
    private string _start = "0:00";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    [NotifyPropertyChangedFor(nameof(Problem))]
    private string _end = string.Empty;

    [ObservableProperty]
    private bool _precise;

    [ObservableProperty]
    private string _length = string.Empty;

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

    /// <param name="settings">Video quality and speed for a precise cut, which encodes.</param>
    public TrimViewModel(IEnumerable<string> files, string? ffmpegPath, ConversionSettings settings)
    {
        _files = files.Where(f => File.Exists(f) && MediaTrim.Inputs.Contains(Formats.ExtensionOf(f))).ToList();
        _ffmpeg = ffmpegPath is null ? null : new FfmpegConverter(ffmpegPath);
        _settings = settings;
        Title = string.Format(Strings.Trim_Title, _files.Count);
        HasVideo = _files.Any(f => Formats.VideoInputs.Contains(Formats.ExtensionOf(f)));
        if (_files.Count == 0)
        {
            Status = Strings.Trim_NoFiles;
        }
        else if (_ffmpeg is null)
        {
            Status = Strings.Trim_NoFfmpeg;
        }
        else
        {
            _ = ShowLengthAsync(_files[0]);
        }
    }

    /// <summary>Raised when every file is done, or on close.</summary>
    public event Action? CloseRequested;

    public string Title { get; }

    /// <summary>Sound is always copied, so the precise choice only matters for videos.</summary>
    public bool HasVideo { get; }

    /// <summary>What is wrong with the times as written; empty when they can be used.</summary>
    public string Problem
    {
        get
        {
            var (start, end, ok) = Times();
            return !ok ? Strings.Trim_BadTime : end <= start ? Strings.Trim_EndBeforeStart : string.Empty;
        }
    }

    private (TimeSpan Start, TimeSpan? End, bool Ok) Times()
    {
        var start = MediaTrim.ParseTime(Start);
        var end = string.IsNullOrWhiteSpace(End) ? null : MediaTrim.ParseTime(End);
        var ok = start is not null && (string.IsNullOrWhiteSpace(End) || end is not null);
        return (start ?? TimeSpan.Zero, end, ok);
    }

    private async Task ShowLengthAsync(string file)
    {
        var name = Path.GetFileName(file);
        TimeSpan? length = null;
        try
        {
            length = await Task.Run(() => _ffmpeg!.ProbeDurationAsync(file));
        }
        catch (Exception e) when (e is ConversionException or IOException or System.ComponentModel.Win32Exception)
        {
            // Shown as unknown; cutting reports the real problem.
        }

        Length = length is { } l ? string.Format(Strings.Trim_Length, name, MediaTrim.FormatTime(l))
            : string.Format(Strings.Trim_LengthUnknown, name);
    }

    private bool CanApply() => !IsBusy && _ffmpeg is not null && _files.Count > 0 && Problem.Length == 0;

    [RelayCommand(CanExecute = nameof(CanApply))]
    private async Task ApplyAsync()
    {
        var (start, end, _) = Times();
        var precise = Precise && HasVideo;
        var suffix = Strings.Trim_FileSuffix;
        IsBusy = true;
        Errors = string.Empty;
        Progress = 0;
        _cancel = new CancellationTokenSource();
        var failed = new List<string>();
        for (var i = 0; i < _files.Count && !_cancel.IsCancellationRequested; i++)
        {
            Status = string.Format(Strings.Watermark_Progress, i + 1, _files.Count);
            var input = _files[i];
            var done = i;
            var progress = new Progress<double?>(f => Dispatcher.UIThread.Post(() => Progress = (done + (f ?? 0)) / _files.Count));
            try
            {
                await Task.Run(() => MediaTrim.TrimAsync(_ffmpeg!, input, MediaTrim.OutputPathFor(input, suffix, precise),
                    start, end, precise, _settings, progress, _cancel.Token));
            }
            catch (ConversionException e)
            {
                failed.Add($"{Path.GetFileName(input)}: {LastLine(e.Message)}");
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        IsBusy = false;
        var cancelled = _cancel.IsCancellationRequested;
        _cancel.Dispose();
        _cancel = null;
        if (failed.Count == 0 || cancelled)
        {
            CloseRequested?.Invoke();
            return;
        }

        Status = string.Format(Strings.Trim_Failed, failed.Count, _files.Count);
        Errors = string.Join(Environment.NewLine, failed);
    }

    /// <summary>While cutting, stops (the unfinished file is removed) and closes.</summary>
    [RelayCommand]
    private void Close()
    {
        if (_cancel is not null)
        {
            _cancel.Cancel();
            return;
        }

        CloseRequested?.Invoke();
    }

    // An ffmpeg error ends with its last lines of output, the last of which says what went wrong.
    private static string LastLine(string message) =>
        message.Split(Environment.NewLine).LastOrDefault(line => !string.IsNullOrWhiteSpace(line)) ?? message;
}
