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

        using var runner = ConversionRunner.CreateDefault(Ffmpeg, 1);
        await runner.RunAsync([job]);

        Assert.True(job.State == JobState.Done, job.Error);
        Assert.True(new FileInfo(job.Output!).Length > 0);
        // Intermediate values depend on timing (ffmpeg reports every 0.5 s), so only the end is checked.
        Assert.Equal(1, progress[^1]);
    }

    [Theory]
    [InlineData("720p/mp4", "320x240")]
    [InlineData("scale50/mp4", "160x120")]
    [InlineData("rotate-right/mp4", "240x320")]
    public async Task VideoTransforms_ProduceExpectedSize_AndNeverUpscale(string presetId, string expectedSize)
    {
        var ffprobe = ToolLocator.Find("ffprobe");
        if (Ffmpeg is null || ffprobe is null)
        {
            return;
        }

        var input = Path.Combine(_dir, "sample.mov");
        GenerateSampleVideo(input);
        var job = new ConversionJob(input, PresetCatalog.Find(PresetCatalog.Default, presetId)!);

        using var runner = ConversionRunner.CreateDefault(Ffmpeg, 1);
        await runner.RunAsync([job]);

        Assert.True(job.State == JobState.Done, job.Error);
        Assert.Equal(expectedSize, Run(ffprobe, "-v", "error", "-select_streams", "v:0", "-show_entries", "stream=width,height", "-of", "csv=p=0:s=x", job.Output!).Trim());
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

        using var runner = ConversionRunner.CreateDefault(Ffmpeg, 1);
        await runner.RunAsync([job]);

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

        using var runner = ConversionRunner.CreateDefault(null, 2);
        await runner.RunAsync(jobs);

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

        using var runner = ConversionRunner.CreateDefault(null, 1);
        await runner.RunAsync([job]);

        Assert.True(job.State == JobState.Done, job.Error);
        using var result = new MagickImage(job.Output!);
        Assert.Equal((uint)width, result.Width);
        Assert.Equal((uint)height, result.Height);
    }

    [Fact]
    public async Task RemoveMetadata_DropsExifAndLocation_KeepsOrientationColorsAndQuality()
    {
        // A phone photo: stored sideways with an orientation tag, GPS position, a comment and an sRGB profile.
        var input = Path.Combine(_dir, "telefon.jpg");
        using (var photo = new MagickImage(MagickColors.Orange, 400, 200))
        {
            var exif = new ExifProfile();
            exif.SetValue(ExifTag.Orientation, (ushort)6); // display rotated 90° clockwise
            exif.SetValue(ExifTag.Make, "Telefon");
            exif.SetValue(ExifTag.GPSLatitude, [new Rational(41, 1), new Rational(0, 1), new Rational(0, 1)]);
            exif.SetValue(ExifTag.GPSLatitudeRef, "N");
            photo.SetProfile(exif);
            photo.SetProfile(ColorProfiles.SRGB);
            photo.Orientation = ImageMagick.Orientation.RightTop; // what Magick writes as the EXIF orientation
            photo.Comment = "gizli";
            photo.Quality = 80;
            photo.Write(input);
        }

        using (var written = new MagickImage(input))
        {
            Assert.Equal(ImageMagick.Orientation.RightTop, written.Orientation);
            Assert.NotNull(written.GetExifProfile()?.GetValue(ExifTag.GPSLatitude));
        }

        var job = new ConversionJob(input, PresetCatalog.Find(PresetCatalog.Default, "strip-metadata/jpg")!);
        using var runner = ConversionRunner.CreateDefault(null, 1);
        await runner.RunAsync([job]);

        Assert.True(job.State == JobState.Done, job.Error);
        Assert.Equal(Path.Combine(_dir, "telefon (no metadata).jpg"), job.Output);
        using var result = new MagickImage(job.Output!);
        Assert.Null(result.GetExifProfile());
        Assert.Null(result.GetXmpProfile());
        Assert.Null(result.GetIptcProfile());
        Assert.True(string.IsNullOrEmpty(result.Comment));
        Assert.NotNull(result.GetColorProfile());
        Assert.Equal((200u, 400u), (result.Width, result.Height));
        Assert.Equal(80u, result.Quality);
    }

    [Fact]
    public async Task MultiPagePdf_BecomesOneImagePerPage()
    {
        var input = Path.Combine(_dir, "document.pdf");
        CreatePdf(input, (144, 72), (72, 144), (72, 72));
        var job = new ConversionJob(input, PresetCatalog.Find(PresetCatalog.Default, "png")!);

        using var runner = ConversionRunner.CreateDefault(null, 1);
        await runner.RunAsync([job]);

        Assert.True(job.State == JobState.Done, job.Error);
        Assert.False(File.Exists(job.Output));
        // 150 dpi: a 72 pt (one inch) side becomes 150 px.
        (string Name, uint Width, uint Height)[] expected =
            [("document-01.png", 300, 150), ("document-02.png", 150, 300), ("document-03.png", 150, 150)];
        foreach (var (name, width, height) in expected)
        {
            using var page = new MagickImage(Path.Combine(_dir, name));
            Assert.Equal(width, page.Width);
            Assert.Equal(height, page.Height);
        }
    }

    [Fact]
    public async Task SinglePagePdf_IsWrittenToTheOutput()
    {
        var input = Path.Combine(_dir, "page.pdf");
        CreatePdf(input, (72, 72));
        var job = new ConversionJob(input, PresetCatalog.Find(PresetCatalog.Default, "jpg")!);

        using var runner = ConversionRunner.CreateDefault(null, 1);
        await runner.RunAsync([job]);

        Assert.True(job.State == JobState.Done, job.Error);
        using var result = new MagickImage(job.Output!);
        Assert.Equal(150u, result.Width);
    }

    [Fact]
    public async Task DamagedPdf_Fails()
    {
        var input = Path.Combine(_dir, "broken.pdf");
        File.WriteAllText(input, "not a pdf");
        var job = new ConversionJob(input, PresetCatalog.Find(PresetCatalog.Default, "png")!);

        using var runner = ConversionRunner.CreateDefault(null, 1);
        await runner.RunAsync([job]);

        Assert.Equal(JobState.Failed, job.State);
        Assert.Empty(Directory.GetFiles(_dir, "*.png"));
    }

    [Fact]
    public async Task OfficeToPdf_WithoutLibreOffice_FailsWithHint()
    {
        var input = Path.Combine(_dir, "letter.rtf");
        CreateRtf(input);
        var job = new ConversionJob(input, PresetCatalog.Find(PresetCatalog.Default, "pdf")!);

        using var runner = ConversionRunner.CreateDefault(null, 1);
        await runner.RunAsync([job]);

        Assert.Equal(JobState.Failed, job.State);
        Assert.Contains("LibreOffice", job.Error);
    }

    [Fact]
    public async Task OfficeToPdf_WithLibreOffice_CreatesPdf()
    {
        if (ToolLocator.FindLibreOffice() is not { } soffice)
        {
            return;
        }

        var input = Path.Combine(_dir, "mektup ğüş.rtf");
        CreateRtf(input);
        var pdf = PresetCatalog.Find(PresetCatalog.Default, "pdf")!;
        ConversionJob[] jobs = [new(input, pdf), new(input, pdf)];

        using var runner = ConversionRunner.CreateDefault(null, 2, soffice);
        await runner.RunAsync(jobs);

        Assert.All(jobs, j => Assert.True(j.State == JobState.Done, j.Error));
        Assert.Contains(Path.Combine(_dir, "mektup ğüş.pdf"), jobs.Select(j => j.Output));
        Assert.All(jobs, j => Assert.StartsWith("%PDF", File.ReadAllText(j.Output!)));
    }

    [Theory]
    // A4 at 300 dpi becomes an A4 page (842 x 595 pt); a small 72 dpi image keeps its size.
    [InlineData("scan.png", 3508, 2480, 842, 595)]
    [InlineData("photo.jpg", 3508, 2480, 842, 595)]
    [InlineData("icon.png", 200, 100, 200, 100)]
    public async Task ImageToPdf_FitsTheLongerSideToA4(string name, int width, int height, int pageWidth, int pageHeight)
    {
        var input = Path.Combine(_dir, name);
        CreateImage(input, width, height);
        var job = new ConversionJob(input, PresetCatalog.Find(PresetCatalog.Default, "pdf")!);

        using var runner = ConversionRunner.CreateDefault(null, 1);
        await runner.RunAsync([job]);

        Assert.True(job.State == JobState.Done, job.Error);
        using var document = PdfiumDocument.Open(job.Output!);
        Assert.Equal(1, document.PageCount);
        var page = document.PageSize(0);
        Assert.InRange(page.Width, pageWidth - 1, pageWidth + 1);
        Assert.InRange(page.Height, pageHeight - 1, pageHeight + 1);
    }

    [Fact]
    public async Task CombinedPdf_HasOnePagePerImage_InTheGivenOrder()
    {
        string[] inputs = [Path.Combine(_dir, "wide.png"), Path.Combine(_dir, "tall.jpg"), Path.Combine(_dir, "square.webp")];
        CreateImage(inputs[0], 200, 100);
        CreateImage(inputs[1], 100, 200);
        CreateImage(inputs[2], 150, 150);
        var job = new ConversionJob(inputs, PresetCatalog.Find(PresetCatalog.Default, "combine-pdf")!);

        using var runner = ConversionRunner.CreateDefault(null, 1);
        await runner.RunAsync([job]);

        Assert.True(job.State == JobState.Done, job.Error);
        Assert.Equal(Path.Combine(_dir, "wide (combined).pdf"), job.Output);
        using var document = PdfiumDocument.Open(job.Output!);
        Assert.Equal(3, document.PageCount);
        (int Width, int Height)[] expected = [(200, 100), (100, 200), (150, 150)];
        for (var i = 0; i < expected.Length; i++)
        {
            var (width, height) = document.PageSize(i);
            Assert.InRange(width, expected[i].Width - 1, expected[i].Width + 1);
            Assert.InRange(height, expected[i].Height - 1, expected[i].Height + 1);
        }
    }

    [Fact]
    public async Task CombinedPdf_WithAnUnreadableImage_FailsWithoutOutput()
    {
        var good = Path.Combine(_dir, "good.png");
        var bad = Path.Combine(_dir, "bad.png");
        CreateImage(good, 10, 10);
        File.WriteAllText(bad, "not an image");
        var job = new ConversionJob([good, bad], PresetCatalog.Find(PresetCatalog.Default, "combine-pdf")!);

        using var runner = ConversionRunner.CreateDefault(null, 1);
        await runner.RunAsync([job]);

        Assert.Equal(JobState.Failed, job.State);
        Assert.Empty(Directory.GetFiles(_dir, "*.pdf"));
    }

    [Fact]
    public void SeveralInputs_NeedACombiningPreset()
    {
        Assert.Throws<ArgumentException>(() =>
            new ConversionJob(["a.png", "b.png"], PresetCatalog.Find(PresetCatalog.Default, "pdf")!));
    }

    [Fact]
    public async Task PdfPreset_MixedSelection_ConvertsImagesWithoutLibreOffice()
    {
        var image = Path.Combine(_dir, "photo.png");
        var letter = Path.Combine(_dir, "letter.rtf");
        CreateImage(image, 100, 100);
        CreateRtf(letter);
        var pdf = PresetCatalog.Find(PresetCatalog.Default, "pdf")!;
        ConversionJob[] jobs = [new(image, pdf), new(letter, pdf)];

        using var runner = ConversionRunner.CreateDefault(null, 1);
        await runner.RunAsync(jobs);

        Assert.True(jobs[0].State == JobState.Done, jobs[0].Error);
        Assert.Equal(JobState.Failed, jobs[1].State);
        Assert.Contains("LibreOffice", jobs[1].Error);
    }

    [Fact]
    public async Task OfficeToPdf_GivesUpAfterTheTimeout()
    {
        if (ToolLocator.FindLibreOffice() is not { } soffice)
        {
            return;
        }

        var input = Path.Combine(_dir, "letter.rtf");
        CreateRtf(input);
        var converter = new Office.OfficeConverter(soffice, TimeSpan.FromSeconds(1));
        var output = Path.Combine(_dir, "letter.pdf");

        // Starting LibreOffice with a fresh profile takes longer than a second.
        var e = await Assert.ThrowsAsync<ConversionException>(() => converter.ConvertAsync(
            input, output, PresetCatalog.Find(PresetCatalog.Default, "pdf")!, new Progress<double?>(), CancellationToken.None));

        Assert.Contains("did not finish", e.Message);
        Assert.False(File.Exists(output));
    }

    [Fact]
    public async Task CanceledBeforeStart_IsCanceled()
    {
        var input = Path.Combine(_dir, "image.png");
        CreateImage(input, 10, 10);
        var job = new ConversionJob(input, PresetCatalog.Find(PresetCatalog.Default, "jpg")!);
        job.Cancel();

        using var runner = ConversionRunner.CreateDefault(null, 1);
        await runner.RunAsync([job]);

        Assert.Equal(JobState.Canceled, job.State);
    }

    private static void CreateImage(string path, int width, int height)
    {
        using var image = new MagickImage(MagickColors.CornflowerBlue, (uint)width, (uint)height);
        image.Write(path);
    }

    /// <summary>Writes a PDF with one page per size, in points (72 per inch).</summary>
    private static void CreatePdf(string path, params (int Width, int Height)[] pages)
    {
        using var images = new MagickImageCollection();
        foreach (var (width, height) in pages)
        {
            images.Add(new MagickImage(MagickColors.CornflowerBlue, (uint)width, (uint)height) { Density = new Density(72) });
        }

        images.Write(path, MagickFormat.Pdf);
    }

    private static void CreateRtf(string path) =>
        File.WriteAllText(path, @"{\rtf1\ansi{\fonttbl\f0 Arial;}\f0\fs24 Hello from royconvert.\par}");

    private static void GenerateSampleVideo(string path) =>
        Run(Ffmpeg!, "-hide_banner", "-y", "-f", "lavfi", "-i", "testsrc=duration=3:size=320x240:rate=25",
            "-f", "lavfi", "-i", "sine=frequency=440:duration=3", "-shortest", path);

    /// <summary>Runs a tool, asserts success and returns its standard output.</summary>
    private static string Run(string tool, params string[] args)
    {
        var info = new ProcessStartInfo(tool)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var arg in args)
        {
            info.ArgumentList.Add(arg);
        }

        using var process = Process.Start(info)!;
        var stderr = process.StandardError.ReadToEndAsync();
        var stdout = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, stderr.Result);
        return stdout;
    }
}
