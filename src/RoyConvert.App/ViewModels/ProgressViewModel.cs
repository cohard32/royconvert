using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RoyConvert.App.Resources;
using RoyConvert.Core;

namespace RoyConvert.App.ViewModels;

public partial class ProgressViewModel : ViewModelBase, IDisposable
{
    private readonly ConversionRunner _runner;
    private readonly IReadOnlyList<Preset> _presets;
    private readonly AppSettings _settings;

    // Arguments received from another process but not added yet (posted to the UI thread).
    private int _incoming;

    [ObservableProperty]
    private string _summary = string.Empty;

    [ObservableProperty]
    private bool _allFinished;

    /// <param name="settings">The user's quality choices, applied to every job.</param>
    public ProgressViewModel(ConversionRunner runner, IReadOnlyList<Preset> presets, AppSettings? settings = null)
    {
        _runner = runner;
        _presets = presets;
        _settings = settings ?? new AppSettings();
        _runner.Idle += () => Dispatcher.UIThread.Post(OnIdle);
    }

    public ObservableCollection<JobViewModel> Jobs { get; } = [];

    /// <summary>Errors that are not tied to a job, such as an unknown preset id.</summary>
    public ObservableCollection<string> Messages { get; } = [];

    /// <summary>Raised when every job finished successfully, so the window can close itself.</summary>
    public event Action? Completed;

    /// <summary>
    /// Every job finished successfully and nothing received from another process is still waiting to be added:
    /// the window may close.
    /// </summary>
    public bool IsComplete =>
        Volatile.Read(ref _incoming) == 0 && Messages.Count == 0 && Jobs.All(j => j.IsDone);

    /// <summary>Thread-safe: arguments from another process, added on the UI thread.</summary>
    public void Receive(string[] args)
    {
        Interlocked.Increment(ref _incoming);
        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                Add(CommandLine.Parse(args));
            }
            finally
            {
                Interlocked.Decrement(ref _incoming);
            }
        });
    }

    public void Add(CommandLine command)
    {
        if (command.Kind != CommandKind.Convert)
        {
            AddMessage(command.Error ?? string.Format(Strings.Progress_UnsupportedCommand, command.Kind));
            return;
        }

        if (PresetCatalog.Find(_presets, command.PresetId!) is not { } preset)
        {
            AddMessage(string.Format(Strings.Progress_UnknownPreset, command.PresetId));
            return;
        }

        Add(preset, command.Files);
    }

    private void AddMessage(string message)
    {
        Messages.Add(message);
        UpdateSummary();
    }

    public void Add(Preset preset, IEnumerable<string> files)
    {
        preset = PresetNames.WithLocalizedFileName(preset) with { Settings = _settings.ApplyTo(preset.Settings) };
        IEnumerable<ConversionJob> jobs = preset.CombinesInputs
            ? [new ConversionJob(files.Order(NaturalComparer.Instance).ToList(), preset)]
            : files.Select(file => new ConversionJob(file, preset));

        foreach (var job in jobs)
        {
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
        if (AllFinished && IsComplete)
        {
            Completed?.Invoke();
        }
    }

    private void UpdateSummary()
    {
        var done = Jobs.Count(j => j.IsDone);
        var failed = Jobs.Count(j => j.IsFailed);
        AllFinished = Jobs.All(j => j.IsFinished);
        Summary = Jobs.Count == 0 ? string.Empty : failed > 0
            ? string.Format(Strings.Progress_SummaryFailed, done, Jobs.Count, failed)
            : string.Format(Strings.Progress_Summary, done, Jobs.Count);
    }
}
