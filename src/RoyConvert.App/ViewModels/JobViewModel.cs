using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RoyConvert.App.Platform;
using RoyConvert.Core;

namespace RoyConvert.App.ViewModels;

public partial class JobViewModel : ViewModelBase
{
    [ObservableProperty]
    private double _percent;

    [ObservableProperty]
    private bool _isIndeterminate;

    [ObservableProperty]
    private string _status = string.Empty;

    [ObservableProperty]
    private JobState _state;

    [ObservableProperty]
    private string? _error;

    public JobViewModel(ConversionJob job)
    {
        Job = job;
        job.Changed += _ => Dispatcher.UIThread.Post(Refresh);
        Refresh();
    }

    public ConversionJob Job { get; }

    public string FileName => Path.GetFileName(Job.Input);

    public string PresetName => Job.Preset.Name.Replace("/", " › ");

    public bool IsFinished => State is JobState.Done or JobState.Failed or JobState.Canceled;

    public bool IsDone => State == JobState.Done;

    public bool IsFailed => State == JobState.Failed;

    [RelayCommand]
    private void Cancel() => Job.Cancel();

    [RelayCommand]
    private void ShowOutput()
    {
        if (Job.Output is { } output)
        {
            FileManager.Reveal(output);
        }
    }

    private void Refresh()
    {
        State = Job.State;
        Error = Job.Error;
        IsIndeterminate = Job.State == JobState.Running && Job.Progress is null;
        Percent = (Job.Progress ?? 0) * 100;
        Status = Job.State switch
        {
            JobState.Queued => "Waiting",
            JobState.Running when Job.Progress is { } p => $"{p:P0}",
            JobState.Running => "Converting",
            JobState.Done => "Done",
            JobState.Failed => "Failed",
            _ => "Canceled",
        };
        OnPropertyChanged(nameof(IsFinished));
        OnPropertyChanged(nameof(IsDone));
        OnPropertyChanged(nameof(IsFailed));
    }
}
