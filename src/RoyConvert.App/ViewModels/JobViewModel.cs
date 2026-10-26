using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RoyConvert.App.Platform;
using RoyConvert.App.Resources;
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

    public string FileName => Job.Inputs.Count == 1
        ? Path.GetFileName(Job.Input)
        : string.Format(Strings.Job_MoreFiles, Path.GetFileName(Job.Input), Job.Inputs.Count - 1);

    public string PresetName => PresetNames.DisplayName(Job.Preset).Replace("/", " › ");

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
            JobState.Queued => Strings.Job_Waiting,
            JobState.Running when Job.Progress is { } p => $"{p:P0}",
            JobState.Running => Strings.Job_Converting,
            JobState.Done => Strings.Job_Done,
            JobState.Failed => Strings.Job_Failed,
            _ => Strings.Job_Canceled,
        };
        OnPropertyChanged(nameof(IsFinished));
        OnPropertyChanged(nameof(IsDone));
        OnPropertyChanged(nameof(IsFailed));
    }
}
