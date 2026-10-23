namespace RoyConvert.Core;

public enum JobState
{
    Queued,
    Running,
    Done,
    Failed,
    Canceled,
}

/// <summary>One input file converted with one preset. <see cref="Changed"/> fires on worker threads.</summary>
public sealed class ConversionJob(string input, Preset preset)
{
    private readonly CancellationTokenSource _cancel = new();
    private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public string Input { get; } = input;

    public Preset Preset { get; } = preset;

    /// <summary>Chosen when the job starts, so files created by earlier jobs are taken into account.</summary>
    public string? Output { get; private set; }

    public JobState State { get; private set; } = JobState.Queued;

    /// <summary>[0, 1], or null while indeterminate.</summary>
    public double? Progress { get; private set; } = 0;

    public string? Error { get; private set; }

    public bool IsFinished => State is JobState.Done or JobState.Failed or JobState.Canceled;

    /// <summary>Completes (never faults) when the job reaches a finished state.</summary>
    public Task Completion => _completion.Task;

    public event Action<ConversionJob>? Changed;

    /// <summary>Cancels a running job, or makes a queued job finish as canceled without starting.</summary>
    public void Cancel() => _cancel.Cancel();

    internal async Task RunAsync(Func<Preset, IConverter> converterFor, Func<ConversionJob, string> chooseOutput)
    {
        var token = _cancel.Token;
        try
        {
            token.ThrowIfCancellationRequested();
            Output = chooseOutput(this);
            Update(JobState.Running, 0);
            var progress = new SyncProgress(value => Update(JobState.Running, value));
            await converterFor(Preset).ConvertAsync(Input, Output, Preset, progress, token);
            Update(JobState.Done, 1);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            Update(JobState.Canceled, Progress);
        }
        catch (Exception e)
        {
            Error = e.Message;
            Update(JobState.Failed, Progress);
        }
        finally
        {
            _completion.TrySetResult();
        }
    }

    private void Update(JobState state, double? progress)
    {
        // A late progress report from the converter must not overwrite a finished state.
        if (IsFinished)
        {
            return;
        }

        State = state;
        Progress = progress;
        Changed?.Invoke(this);
    }

    /// <summary>Unlike <see cref="Progress{T}"/>, reports synchronously on the calling thread, keeping updates ordered.</summary>
    private sealed class SyncProgress(Action<double?> report) : IProgress<double?>
    {
        public void Report(double? value) => report(value);
    }
}
