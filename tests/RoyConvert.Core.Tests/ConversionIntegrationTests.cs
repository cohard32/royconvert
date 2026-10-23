using System.Diagnostics;
using ImageMagick;

namespace RoyConvert.Core.Tests;

/// <summary>
/// End-to-end conversions with the real ffmpeg and Magick.NET. ffmpeg tests do nothing when
/// ffmpeg is not installed (set ROYCONVERT_FFMPEG or put it on PATH).
/// </summary>
public sealed class ConversionIntegrationTests : IDisposable
{
    private static readonly string? Ffmpeg = ToolLocator.FindFfmpeg();

    private readonly string _dir = Directory.CreateTempSubdirectory("royconvert-tests-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Theory]
    [InlineData("mp4")]
    [InlineData("webm")]
    [InlineData("gif")]
    [InlineData("mp3")]
    [InlineData("720p/mkv")]
    [InlineData("rotate-left/avi")]
    public async Task ConvertsGeneratedVideo(string presetId)
    {
        if (Ffmpeg is null)
        {
            return;
        }

        var input = Path.Combine(_dir, "sample.mov");
        GenerateSampleVideo(input);
        var job = new ConversionJob(input, PresetCatalog.Find(PresetCatalog.Default, presetId)!);
        var progress = new List<double?>();
        job.Changed += j => { lock (progress) { progress.Add(j.Progress); } };

        await ConversionRunner.CreateDefault(Ffmpeg).RunAsync([job], 1);

        Assert.True(job.State == JobState.Done, job.Error);
        Assert.True(new FileInfo(job.Output!).Length > 0);
        // Intermediate values depend on timing (ffmpeg reports every 0.5 s), so only the end is checked.
        Assert.Equal(1, progress[^1]);
    }

    [Fact]
    public async Task FailedConversion_ReportsErrorAndLeavesNoOutput()
    {
        if (Ffmpeg is null)
        {
            return;
        }

        var input = Path.Combine(_dir, "broken.mp4");
        await File.WriteAllTextAsync(input, "not a video");
        var job = new ConversionJob(input, PresetCatalog.Find(PresetCatalog.Default, "mkv")!);

        await ConversionRunner.CreateDefault(Ffmpeg).RunAsync([job], 1);

        Assert.Equal(JobState.Failed, job.State);
        Assert.Contains("ffmpeg exited", job.Error);
        Assert.False(File.Exists(job.Output));
    }

    [Fact]
    public async Task ParallelJobs_WithSameStem_GetDistinctOutputs()
    {
        var a = Path.Combine(_dir, "pic.bmp");
        var b = Path.Combine(_dir, "pic.tga");
        CreateImage(a, 64, 32);
        CreateImage(b, 64, 32);
        var png = PresetCatalog.Find(PresetCatalog.Default, "png")!;
        ConversionJob[] jobs = [new(a, png), new(b, png)];

        await ConversionRunner.CreateDefault(null).RunAsync(jobs, 2);

        Assert.All(jobs, j => Assert.Equal(JobState.Done, j.State));
        Assert.NotEqual(jobs[0].Output, jobs[1].Output);
    }

    [Theory]
    [InlineData("jpg", 400, 200)]
    [InlineData("scale50/webp", 200, 100)]
    [InlineData("rotate-right/png", 200, 400)]
    [InlineData("ico", 256, 128)]
    public async Task ConvertsImages(string presetId, int width, int height)
    {
        var input = Path.Combine(_dir, "image.png");
        CreateImage(input, 400, 200);
        var job = new ConversionJob(input, PresetCatalog.Find(PresetCatalog.Default, presetId)!);

        await ConversionRunner.CreateDefault(null).RunAsync([job], 1);

        Assert.True(job.State == JobState.Done, job.Error);
        using var result = new MagickImage(job.Output!);
        Assert.Equal((uint)width, result.Width);
        Assert.Equal((uint)height, result.Height);
    }

    [Fact]
    public async Task CanceledBeforeStart_IsCanceled()
    {
        var input = Path.Combine(_dir, "image.png");
        CreateImage(input, 10, 10);
        var job = new ConversionJob(input, PresetCatalog.Find(PresetCatalog.Default, "jpg")!);
        job.Cancel();

        await ConversionRunner.CreateDefault(null).RunAsync([job], 1);

        Assert.Equal(JobState.Canceled, job.State);
    }

    private static void CreateImage(string path, int width, int height)
    {
        using var image = new MagickImage(MagickColors.CornflowerBlue, (uint)width, (uint)height);
        image.Write(path);
    }

    private static void GenerateSampleVideo(string path)
    {
        var info = new ProcessStartInfo(Ffmpeg!)
        {
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var arg in new[]
                 {
                     "-hide_banner", "-y", "-f", "lavfi", "-i", "testsrc=duration=3:size=320x240:rate=25",
                     "-f", "lavfi", "-i", "sine=frequency=440:duration=3", "-shortest", path,
                 })
        {
            info.ArgumentList.Add(arg);
        }

        using var process = Process.Start(info)!;
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, stderr);
    }
}
