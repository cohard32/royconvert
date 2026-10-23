namespace RoyConvert.Core.Tests;

public class ConversionRunnerTests
{
    private static readonly Preset Mp4 = PresetCatalog.Find(PresetCatalog.Default, "mp4")!;
    private static readonly string Input = Path.Combine(Path.GetTempPath(), "royconvert-missing", "a.mov");

    [Fact]
    public async Task JobsAddedLater_StillRun_AndIdleFiresEachTime()
    {
        var converter = new FakeConverter();
        using var runner = new ConversionRunner(_ => converter, 2);
        var idle = 0;
        runner.Idle += () => Interlocked.Increment(ref idle);

        await runner.RunAsync([new ConversionJob(Input, Mp4), new ConversionJob(Input, Mp4)]);
        await WaitUntil(() => Volatile.Read(ref idle) == 1);
        var late = new ConversionJob(Input, Mp4);
        await runner.RunAsync([late]);
        await WaitUntil(() => Volatile.Read(ref idle) == 2);

        Assert.Equal(JobState.Done, late.State);
        Assert.Equal(3, converter.Calls);
    }

    [Fact]
    public async Task ParallelJobs_GetDistinctOutputs()
    {
        using var runner = new ConversionRunner(_ => new FakeConverter(), 4);
        var jobs = Enumerable.Range(0, 4).Select(_ => new ConversionJob(Input, Mp4)).ToList();

        await runner.RunAsync(jobs);

        Assert.Equal(4, jobs.Select(j => j.Output).Distinct().Count());
    }

    [Fact]
    public async Task CancelingRunningJob_EndsCanceled()
    {
        var converter = new FakeConverter { Block = true };
        using var runner = new ConversionRunner(_ => converter, 1);
        var job = new ConversionJob(Input, Mp4);

        var run = runner.RunAsync([job]);
        await WaitUntil(() => job.State == JobState.Running);
        job.Cancel();
        await run;

        Assert.Equal(JobState.Canceled, job.State);
    }

    [Fact]
    public async Task ConverterError_EndsFailedWithMessage()
    {
        using var runner = new ConversionRunner(_ => new FakeConverter { Fail = "boom" }, 1);
        var job = new ConversionJob(Input, Mp4);

        await runner.RunAsync([job]);

        Assert.Equal(JobState.Failed, job.State);
        Assert.Equal("boom", job.Error);
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "Timed out.");
            await Task.Delay(10);
        }
    }

    private sealed class FakeConverter : IConverter
    {
        private int _calls;

        public bool Block { get; init; }

        public string? Fail { get; init; }

        public int Calls => Volatile.Read(ref _calls);

        public async Task ConvertAsync(string input, string output, Preset preset, IProgress<double?> progress, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            progress.Report(0.5);
            if (Fail is not null)
            {
                throw new ConversionException(Fail);
            }

            await Task.Delay(Block ? Timeout.Infinite : 10, cancellationToken);
        }
    }
}
