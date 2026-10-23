using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RoyConvert.Core;

namespace RoyConvert.App.ViewModels;

public partial class ProgressViewModel : ViewModelBase, IDisposable
{
    private readonly ConversionRunner _runner;
    private readonly IReadOnlyList<Preset> _presets;

    [ObservableProperty]
    private string _summary = string.Empty;

    [ObservableProperty]
    private bool _allFinished;

    public ProgressViewModel(ConversionRunner runner, IReadOnlyList<Preset> presets)
    {
        _runner = runner;
        _presets = presets;
        _runner.Idle += () => Dispatcher.UIThread.Post(OnIdle);
    }

    public ObservableCollection<JobViewModel> Jobs { get; } = [];

    /// <summary>Errors that are not tied to a job, such as an unknown preset id.</summary>
    public ObservableCollection<string> Messages { get; } = [];

    /// <summary>Raised when every job finished successfully, so the window can close itself.</summary>
    public event Action? Completed;

    public void Add(CommandLine command)
    {
        if (command.Kind != CommandKind.Convert)
        {
            Messages.Add(command.Error ?? $"Unsupported command: {command.Kind}");
            return;
        }

        if (PresetCatalog.Find(_presets, command.PresetId!) is not { } preset)
        {
            Messages.Add($"Unknown preset \"{command.PresetId}\".");
            return;
        }

        Add(preset, command.Files);
    }

    public void Add(Preset preset, IEnumerable<string> files)
    {
        foreach (var file in files)
        {
            var job = new ConversionJob(file, preset);
            var item = new JobViewModel(job);
            item.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(JobViewModel.State))
                {
                    UpdateSummary();
                }
            };
            Jobs.Add(item);
            _runner.Enqueue(job);
        }

        UpdateSummary();
    }

    public void Dispose()
    {
        CancelAll();
        _runner.Dispose();
    }

    [RelayCommand]
    private void CancelAll()
    {
        foreach (var job in Jobs)
        {
            job.Job.Cancel();
        }
    }

    private void OnIdle()
    {
        UpdateSummary();
        // Idle is posted from a worker; more jobs may have been added in between.
        if (AllFinished && Messages.Count == 0 && Jobs.All(j => j.IsDone))
        {
            Completed?.Invoke();
        }
    }

    private void UpdateSummary()
    {
        var done = Jobs.Count(j => j.IsDone);
        var failed = Jobs.Count(j => j.IsFailed);
        AllFinished = Jobs.All(j => j.IsFinished);
        Summary = failed > 0
            ? $"{done} of {Jobs.Count} converted, {failed} failed"
            : $"{done} of {Jobs.Count} converted";
    }
}
