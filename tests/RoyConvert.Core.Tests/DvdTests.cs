using System.Diagnostics;
using RoyConvert.Core.Dvd;
using RoyConvert.Core.Ffmpeg;

namespace RoyConvert.Core.Tests;

/// <summary>
/// DVD → MP4/MKV with fake discs: a VIDEO_TS folder whose VOBs are made by ffmpeg in DVD format (PAL MPEG-2,
/// interlaced, two audio languages) and cut into parts like a real disc. ffmpeg tests do nothing without ffmpeg.
/// </summary>
public sealed class DvdTests : IDisposable
{
    private static readonly string? Ffmpeg = ToolLocator.FindFfmpeg();

    private readonly string _dir = Directory.CreateTempSubdirectory("royconvert-tests-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void Open_FindsTheLargestTitle_WithItsPartsInOrder()
    {
        var disc = Path.Combine(_dir, "Tatil 2010");
        var videoTs = Path.Combine(disc, "VIDEO_TS");
        Directory.CreateDirectory(videoTs);
        WriteBytes(Path.Combine(videoTs, "VIDEO_TS.VOB"), 5000);
        WriteBytes(Path.Combine(videoTs, "VTS_01_0.VOB"), 9000); // menu
        WriteBytes(Path.Combine(videoTs, "VTS_01_2.VOB"), 3000);
        WriteBytes(Path.Combine(videoTs, "VTS_01_1.VOB"), 3000);
        WriteBytes(Path.Combine(videoTs, "vts_02_1.vob"), 4000); // a trailer: larger part, smaller title
        File.WriteAllText(Path.Combine(videoTs, "VTS_01_0.IFO"), "ifo");

        var fromFolder = DvdDisc.Open(disc);
        var fromVideoTs = DvdDisc.Open(videoTs + Path.DirectorySeparatorChar);

        Assert.NotNull(fromFolder);
        Assert.Equal(["VTS_01_1.VOB", "VTS_01_2.VOB"], fromFolder.MainTitle.Select(Path.GetFileName));
        Assert.Equal("Tatil 2010", fromFolder.Name);
        Assert.Equal(fromFolder.MainTitle, fromVideoTs!.MainTitle);
        Assert.Equal(Path.Combine(_dir, "Tatil 2010.dvd"), fromFolder.NamingPath);
        Assert.False(fromFolder.IsOpticalDrive);
    }

    [Fact]
    public void Open_IgnoresFoldersAndFilesWithoutDvdVideo()
    {
        var empty = Path.Combine(_dir, "empty", "VIDEO_TS");
        Directory.CreateDirectory(empty);
        WriteBytes(Path.Combine(empty, "VTS_01_0.VOB"), 10); // only a menu
        var file = Path.Combine(_dir, "movie.vob");
        WriteBytes(file, 10);

        Assert.Null(DvdDisc.Open(_dir));
        Assert.Null(DvdDisc.Open(Path.GetDirectoryName(empty)!));
        Assert.Null(DvdDisc.Open(file));
        Assert.Null(DvdDisc.Open(Path.Combine(_dir, "missing")));
    }

    [Fact]
    public void DvdPresets_AreOfferedForDvdFoldersOnly()
    {
        var videoTs = Path.Combine(_dir, "disc", "VIDEO_TS");
        Directory.CreateDirectory(videoTs);
        WriteBytes(Path.Combine(videoTs, "VTS_01_1.VOB"), 10);

        Assert.Equal(["dvd/mkv", "dvd/mp4"],
            PresetCatalog.ForFiles(PresetCatalog.Default, [Path.GetDirectoryName(videoTs)!]).Select(p => p.Id).Order());
        Assert.Empty(PresetCatalog.ForFiles(PresetCatalog.Default, [_dir]));
        Assert.DoesNotContain(PresetCatalog.ForFiles(PresetCatalog.Default, ["a.mp4"]), p => p.ReadsDvd);
    }

    [Fact]
    public void Arguments_ReadThePartsAsOneStream()
    {
        var disc = new DvdDisc(@"D:\VIDEO_TS", "D", [@"D:\VIDEO_TS\VTS_01_1.VOB", @"D:\VIDEO_TS\VTS_01_2.VOB"], false);

        var args = FfmpegArguments.BuildDvd(disc, "out.mp4", OutputFormat.Mp4, new ConversionSettings());

        Assert.Equal(@"concat:D:\VIDEO_TS\VTS_01_1.VOB|D:\VIDEO_TS\VTS_01_2.VOB", args[args.ToList().IndexOf("-i") + 1]);
        Assert.Contains("0:a?", args);
        Assert.StartsWith("bwdif", args[args.ToList().IndexOf("-vf") + 1]);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            FfmpegArguments.BuildDvd(disc, "out.gif", OutputFormat.Gif, new ConversionSettings()));
    }

    [Theory]
    [InlineData("dvd/mp4", "mp4")]
    [InlineData("dvd/mkv", "mkv")]
    public async Task ConvertsTheMainTitle_OfAFakeDisc(string presetId, string extension)
    {
        if (Ffmpeg is null)
        {
            return;
        }

        var disc = Path.Combine(_dir, "Doğum günü");
        CreateFakeDisc(disc, seconds: 6);
        var job = new ConversionJob(disc, PresetCatalog.Find(PresetCatalog.Default, presetId)!);

        using var runner = ConversionRunner.CreateDefault(Ffmpeg, 1);
        await runner.RunAsync([job]);

        Assert.True(job.State == JobState.Done, job.Error);
        Assert.Equal(Path.Combine(_dir, "Doğum günü." + extension), job.Output);
        var info = Probe(job.Output!);
        var duration = FfmpegProgress.ParseDuration(info.Split('\n').First(l => l.Contains("Duration:")))!.Value;
        Assert.InRange(duration.TotalSeconds, 5.5, 6.5); // both parts, not the 2 s trailer or the menu
        Assert.Equal(2, info.Split('\n').Count(l => l.Contains("Audio:")));
        var video = info.Split('\n').Single(l => l.Contains("Video:"));
        Assert.Contains("h264", video);
        Assert.Contains("progressive", video);
        Assert.Contains("25 fps", video);
        Assert.Contains("DAR 16:9", video); // a widescreen disc stores 720x576 pixels stretched on playback
    }

    [Fact]
    public async Task CopyProtectedOrDamagedDisc_Fails()
    {
        if (Ffmpeg is null)
        {
            return;
        }

        var videoTs = Path.Combine(_dir, "broken", "VIDEO_TS");
        Directory.CreateDirectory(videoTs);
        var random = new Random(1);
        var garbage = new byte[200_000];
        random.NextBytes(garbage);
        File.WriteAllBytes(Path.Combine(videoTs, "VTS_01_1.VOB"), garbage);
        var job = new ConversionJob(Path.GetDirectoryName(videoTs)!, PresetCatalog.Find(PresetCatalog.Default, "dvd/mp4")!);

        using var runner = ConversionRunner.CreateDefault(Ffmpeg, 1);
        await runner.RunAsync([job]);

        Assert.Equal(JobState.Failed, job.State);
        Assert.False(File.Exists(job.Output));
    }

    /// <summary>
    /// A disc folder like a camcorder or recorder DVD: menu, a main title cut in two parts at a sector boundary,
    /// and a short second title.
    /// </summary>
    private static void CreateFakeDisc(string folder, int seconds)
    {
        var videoTs = Path.Combine(folder, "VIDEO_TS");
        Directory.CreateDirectory(videoTs);
        var full = Path.Combine(folder, "full.vob");
        MakeVob(full, seconds);
        var bytes = File.ReadAllBytes(full);
        File.Delete(full);
        var cut = bytes.Length / 2 / 2048 * 2048;
        File.WriteAllBytes(Path.Combine(videoTs, "VTS_01_1.VOB"), bytes[..cut]);
        File.WriteAllBytes(Path.Combine(videoTs, "VTS_01_2.VOB"), bytes[cut..]);
        MakeVob(Path.Combine(videoTs, "VTS_01_0.VOB"), 1);
        MakeVob(Path.Combine(videoTs, "VTS_02_1.VOB"), 2);
        File.WriteAllText(Path.Combine(videoTs, "VIDEO_TS.IFO"), "DVDVIDEO-VMG");
    }

    private static void MakeVob(string path, int seconds) =>
        Run(Ffmpeg!, "-hide_banner", "-y",
            "-f", "lavfi", "-i", $"testsrc=duration={seconds}:size=720x576:rate=25",
            "-f", "lavfi", "-i", $"sine=frequency=440:duration={seconds}",
            "-f", "lavfi", "-i", $"sine=frequency=660:duration={seconds}",
            "-map", "0:v", "-map", "1:a", "-map", "2:a",
            "-target", "pal-dvd", "-aspect", "16:9", "-flags", "+ilme+ildct", "-top", "1", "-shortest", path);

    /// <summary>ffmpeg's description of the file (it exits with an error because no output is given).</summary>
    private static string Probe(string path)
    {
        var startInfo = new ProcessStartInfo(Ffmpeg!) { RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        startInfo.ArgumentList.Add("-hide_banner");
        startInfo.ArgumentList.Add("-i");
        startInfo.ArgumentList.Add(path);
        using var process = Process.Start(startInfo)!;
        var text = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return text;
    }

    private static void Run(string tool, params string[] args)
    {
        var startInfo = new ProcessStartInfo(tool) { RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        using var process = Process.Start(startInfo)!;
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, error);
    }

    private static void WriteBytes(string path, int length) => File.WriteAllBytes(path, new byte[length]);
}
