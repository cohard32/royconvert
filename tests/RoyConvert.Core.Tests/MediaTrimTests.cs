using System.Diagnostics;
using RoyConvert.Core.Ffmpeg;

namespace RoyConvert.Core.Tests;

/// <summary>Cutting a part of a video or sound file, and "Remove sound". ffmpeg tests do nothing without ffmpeg.</summary>
public sealed class MediaTrimTests : IDisposable
{
    private static readonly string? Ffmpeg = ToolLocator.FindFfmpeg();
    private readonly string _dir = Directory.CreateTempSubdirectory("royconvert-trim-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Theory]
    [InlineData("75", 75)]
    [InlineData("1:15", 75)]
    [InlineData("0:01:15", 75)]
    [InlineData("1:02:03.5", 3723.5)]
    [InlineData(" 2:03,25 ", 123.25)]
    [InlineData("0", 0)]
    public void ParseTime_ReadsSecondsMinutesHours(string text, double seconds)
    {
        Assert.Equal(TimeSpan.FromSeconds(seconds), MediaTrim.ParseTime(text));
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("1:75")]
    [InlineData("1:2:3:4")]
    [InlineData("1.5:00")]
    [InlineData("-5")]
    [InlineData("1::2")]
    public void ParseTime_RejectsWhatIsNotATime(string text)
    {
        Assert.Null(MediaTrim.ParseTime(text));
    }

    [Theory]
    [InlineData(75, "1:15")]
    [InlineData(3723.5, "1:02:03.5")]
    [InlineData(5, "0:05")]
    [InlineData(61.25, "1:01.25")]
    public void FormatTime_IsReadBackTheSame(double seconds, string text)
    {
        Assert.Equal(text, MediaTrim.FormatTime(TimeSpan.FromSeconds(seconds)));
        Assert.Equal(TimeSpan.FromSeconds(seconds), MediaTrim.ParseTime(text));
    }

    [Theory]
    [InlineData(@"C:\v\tatil.mp4", false, @"C:\v\tatil (kırpılmış).mp4")]
    [InlineData(@"C:\v\tatil.mov", false, @"C:\v\tatil (kırpılmış).mov")]
    [InlineData(@"C:\v\tatil.mov", true, @"C:\v\tatil (kırpılmış).mp4")]
    [InlineData(@"C:\v\film.mkv", true, @"C:\v\film (kırpılmış).mkv")]
    [InlineData(@"C:\v\şarkı.mp3", true, @"C:\v\şarkı (kırpılmış).mp3")]
    public void OutputPath_KeepsTheFormat_UnlessAPreciseCutCannotWriteIt(string input, bool precise, string expected)
    {
        Assert.Equal(expected, MediaTrim.OutputPathFor(input, " (kırpılmış)", precise, _ => false));
    }

    [Fact]
    public void CopyTrim_SeeksAndCopiesEveryStream()
    {
        var args = FfmpegArguments.BuildTrim("in.mp4", "out.mp4", TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5.5), false, new());

        Assert.Equal(["-ss", "2", "-i", "in.mp4", "-t", "3.5"], args.SkipWhile(a => a != "-ss").Take(6));
        Assert.Contains("copy", args);
        Assert.Equal("out.mp4", args[^1]);
    }

    [Fact]
    public void PreciseTrim_EncodesLikeTheNormalConversion()
    {
        var args = FfmpegArguments.BuildTrim("in.mov", "out.mp4", TimeSpan.Zero, null, true, new());

        Assert.DoesNotContain("-ss", args);
        Assert.DoesNotContain("-t", args);
        Assert.Contains("libx264", args);
        Assert.DoesNotContain("copy", args);
        Assert.Equal("out.mp4", args[^1]);
    }

    [Theory]
    [InlineData("a.mp4", OutputFormat.Mp4, true)]
    [InlineData("a.mkv", OutputFormat.Mkv, true)]
    [InlineData("a.mov", OutputFormat.Mkv, true)]  // MKV holds any codec
    [InlineData("a.mov", OutputFormat.Mp4, false)] // other container: encoded
    [InlineData("a.webm", OutputFormat.Webm, true)]
    public void Mute_CopiesThePicture_WhenTheContainerStays(string input, OutputFormat format, bool copies)
    {
        var mute = PresetCatalog.Find(PresetCatalog.Default, "mute/" + Formats.Extension(format))!;

        var args = FfmpegArguments.Build(input, "out." + Formats.Extension(format), format, mute.Settings);

        Assert.Contains("-an", args);
        Assert.Equal(copies, args.Contains("copy"));
    }

    [Fact]
    public void Mute_IsOfferedForVideos_NotGifsOrSound()
    {
        var mute = PresetCatalog.Find(PresetCatalog.Default, "mute/mp4")!;

        Assert.True(mute.Accepts("a.mov"));
        Assert.False(mute.Accepts("a.gif"));
        Assert.False(mute.Accepts("a.mp3"));
    }

    [Fact]
    public async Task Trim_CopyAndPrecise_GiveThePartAsked()
    {
        if (Ffmpeg is null)
        {
            return;
        }

        var ffmpeg = new FfmpegConverter(Ffmpeg);
        var input = Sample("video.mp4", seconds: 10);
        Assert.Equal(10, (await ffmpeg.ProbeDurationAsync(input))!.Value.TotalSeconds, 1);

        foreach (var precise in new[] { false, true })
        {
            var output = Path.Combine(_dir, $"out-{precise}.mp4");
            var progress = new List<double?>();
            await MediaTrim.TrimAsync(ffmpeg, input, output, TimeSpan.FromSeconds(2.5), TimeSpan.FromSeconds(6), precise, new(),
                new SyncProgress(progress));

            var length = (await ffmpeg.ProbeDurationAsync(output))!.Value.TotalSeconds;
            // A copy starts at the key frame before 2.5 s (every second in the sample), so it may be up to 1 s longer.
            Assert.InRange(length, 3.4, precise ? 3.6 : 4.6);
            Assert.Contains("Audio:", Info(output));
            Assert.Equal(1, progress[^1]);
        }
    }

    [Fact]
    public async Task Trim_SoundFile_FromAPointToTheEnd()
    {
        if (Ffmpeg is null)
        {
            return;
        }

        var ffmpeg = new FfmpegConverter(Ffmpeg);
        var input = Sample("ses.mp3", seconds: 8, video: false);
        var output = Path.Combine(_dir, "out.mp3");

        await MediaTrim.TrimAsync(ffmpeg, input, output, TimeSpan.FromSeconds(5), null, precise: true, new(), new Progress<double?>());

        Assert.Equal(3, (await ffmpeg.ProbeDurationAsync(output))!.Value.TotalSeconds, 0.2);
    }

    [Fact]
    public async Task Trim_OutsideTheFile_IsAnError_WithoutOutput()
    {
        if (Ffmpeg is null)
        {
            return;
        }

        var ffmpeg = new FfmpegConverter(Ffmpeg);
        var input = Sample("video.mp4", seconds: 4);
        var output = Path.Combine(_dir, "out.mp4");

        var error = await Assert.ThrowsAsync<ConversionException>(() =>
            MediaTrim.TrimAsync(ffmpeg, input, output, TimeSpan.FromSeconds(9), null, false, new(), new Progress<double?>()));
        Assert.Contains("0:04", error.Message);
        Assert.False(File.Exists(output));
    }

    [Fact]
    public async Task Mute_LeavesTheVideo_WithoutSound()
    {
        if (Ffmpeg is null)
        {
            return;
        }

        var input = Sample("video.mp4", seconds: 3);
        var job = new ConversionJob(input, PresetCatalog.Find(PresetCatalog.Default, "mute/mp4")!);
        using var runner = ConversionRunner.CreateDefault(Ffmpeg, 1);

        await runner.RunAsync([job]);

        Assert.True(job.State == JobState.Done, job.Error);
        var info = Info(job.Output!);
        Assert.Contains("Video: h264", info);
        Assert.DoesNotContain("Audio:", info);
    }

    /// <summary>A test picture (key frame every second) with a tone, or the tone alone.</summary>
    private string Sample(string name, int seconds, bool video = true)
    {
        var path = Path.Combine(_dir, name);
        string[] args = video
            ? ["-f", "lavfi", "-i", $"testsrc=duration={seconds}:size=320x240:rate=25", "-f", "lavfi", "-i", $"sine=frequency=440:duration={seconds}",
               "-c:v", "libx264", "-g", "25", "-c:a", "aac", "-shortest"]
            : ["-f", "lavfi", "-i", $"sine=frequency=440:duration={seconds}"];
        Run(["-hide_banner", "-y", .. args, path]);
        return path;
    }

    /// <summary>ffmpeg's description of the file's streams.</summary>
    private static string Info(string path) => Run(["-hide_banner", "-i", path], expectSuccess: false);

    private static string Run(string[] args, bool expectSuccess = true)
    {
        var info = new ProcessStartInfo(Ffmpeg!) { RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (var arg in args)
        {
            info.ArgumentList.Add(arg);
        }

        using var process = Process.Start(info)!;
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(!expectSuccess || process.ExitCode == 0, stderr);
        return stderr;
    }

    /// <summary>Records reports as they happen; Progress&lt;T&gt; would post them later.</summary>
    private sealed class SyncProgress(List<double?> values) : IProgress<double?>
    {
        public void Report(double? value)
        {
            lock (values)
            {
                values.Add(value);
            }
        }
    }
}
