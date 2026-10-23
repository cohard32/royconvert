using RoyConvert.Core.Ffmpeg;

namespace RoyConvert.Core.Tests;

public class FfmpegArgumentsTests
{
    private static readonly ConversionSettings Defaults = new();

    private static string Joined(OutputFormat format, ConversionSettings? settings = null) =>
        string.Join(" ", FfmpegArguments.Build("in.mov", "out.x", format, settings ?? Defaults));

    [Fact]
    public void Mp4_UsesX264AndAac_AndEndsWithOutput()
    {
        var args = FfmpegArguments.Build("in.mov", "out.mp4", OutputFormat.Mp4, Defaults);

        Assert.Equal(["-hide_banner", "-nostdin", "-n", "-progress", "pipe:1", "-nostats", "-i", "in.mov"], args.Take(8));
        Assert.Equal("out.mp4", args[^1]);
        var text = string.Join(" ", args);
        Assert.Contains("-c:v libx264 -preset medium -crf 23 -pix_fmt yuv420p", text);
        Assert.Contains("-c:a aac -b:a 192k", text);
        Assert.Contains("-movflags +faststart", text);
    }

    [Fact]
    public void Mkv_HasNoFaststart()
    {
        Assert.DoesNotContain("faststart", Joined(OutputFormat.Mkv));
    }

    [Fact]
    public void Webm_OffsetsCrfForVp9()
    {
        var text = Joined(OutputFormat.Webm);
        Assert.Contains("-c:v libvpx-vp9 -crf 31 -b:v 0", text);
        Assert.Contains("-c:a libopus", text);
    }

    [Fact]
    public void Avi_UsesXvidQuantizer()
    {
        Assert.Contains("-c:v mpeg4 -vtag xvid -q:v 3", Joined(OutputFormat.Avi));
    }

    [Fact]
    public void DisabledAudio_AddsAn()
    {
        var text = Joined(OutputFormat.Mp4, Defaults with { EnableAudio = false });
        Assert.Contains("-an", text);
        Assert.DoesNotContain("-c:a", text);
    }

    [Theory]
    [InlineData(OutputFormat.Mp3, "-c:a libmp3lame -b:a 192k")]
    [InlineData(OutputFormat.Ogg, "-c:a libvorbis -b:a 192k")]
    [InlineData(OutputFormat.Aac, "-c:a aac -b:a 192k")]
    [InlineData(OutputFormat.Flac, "-c:a flac")]
    [InlineData(OutputFormat.Wav, "-c:a pcm_s16le")]
    public void AudioOutputs_DropVideo_AndIgnoreDisabledAudio(OutputFormat format, string expected)
    {
        var text = Joined(format, Defaults with { EnableAudio = false });
        Assert.Contains("-vn", text);
        Assert.Contains(expected, text);
    }

    [Fact]
    public void LosslessAudio_HasNoBitrate()
    {
        Assert.DoesNotContain("-b:a", Joined(OutputFormat.Flac));
    }

    [Fact]
    public void Channels_AreForwarded()
    {
        Assert.Contains("-ac 1", Joined(OutputFormat.Mp3, Defaults with { AudioChannels = 1 }));
    }

    [Theory]
    [InlineData(Rotation.Right, "transpose=1")]
    [InlineData(Rotation.Left, "transpose=2")]
    [InlineData(Rotation.UpsideDown, "hflip,vflip")]
    public void Rotation_MapsToFilter(Rotation rotation, string expected)
    {
        Assert.Equal(expected, FfmpegArguments.VideoFilter(Defaults with { Rotation = rotation }));
    }

    [Fact]
    public void ScaleFactor_KeepsEvenDimensions_WithInvariantDecimal()
    {
        Assert.Equal("scale=trunc(iw*0.75/2)*2:trunc(ih*0.75/2)*2",
            FfmpegArguments.VideoFilter(Defaults with { Scale = Scale.ByFactor(0.75) }));
    }

    [Fact]
    public void ScaleHeight_KeepsAspectRatio()
    {
        Assert.Equal("scale=-2:'min(720,trunc(ih/2)*2)'", FfmpegArguments.VideoFilter(Defaults with { Scale = Scale.ToHeight(720) }));
    }

    [Fact]
    public void RotationIsAppliedBeforeScale()
    {
        Assert.Equal("transpose=2,scale=-2:'min(720,trunc(ih/2)*2)'",
            FfmpegArguments.VideoFilter(Defaults with { Rotation = Rotation.Left, Scale = Scale.ToHeight(720) }));
    }

    [Fact]
    public void NoTransform_HasNoFilter()
    {
        Assert.Null(FfmpegArguments.VideoFilter(Defaults));
        Assert.DoesNotContain("-vf", Joined(OutputFormat.Mp4));
    }

    [Fact]
    public void Gif_BuildsPaletteGraph_AfterTransforms()
    {
        var args = FfmpegArguments.Build("in.mov", "out.gif", OutputFormat.Gif, Defaults with { Scale = Scale.ToHeight(480) });
        var filter = args[args.ToList().IndexOf("-vf") + 1];

        Assert.StartsWith("scale=-2:'min(480,trunc(ih/2)*2)',fps=15,split[a][b];[a]palettegen", filter);
        Assert.Contains("paletteuse", filter);
        Assert.Contains("-an", args);
    }

    [Fact]
    public void ImageFormats_AreRejected()
    {
        Assert.Throws<ArgumentException>(() => FfmpegArguments.Build("a", "b", OutputFormat.Png, Defaults));
    }

    [Fact]
    public void CustomArguments_AreInsertedBeforeOutput()
    {
        var args = FfmpegArguments.Build("in.mov", "out.mp4", OutputFormat.Mp4,
            Defaults with { CustomFfmpegArguments = "-metadata title=\"My video\" -t 5" });

        Assert.Equal(["-metadata", "title=My video", "-t", "5", "out.mp4"], args.TakeLast(5));
    }

    [Theory]
    [InlineData("", new string[0])]
    [InlineData("  -a   b ", new[] { "-a", "b" })]
    [InlineData("\"x y\" z", new[] { "x y", "z" })]
    [InlineData("\"\"", new[] { "" })]
    public void SplitCommandLine_HonorsQuotes(string input, string[] expected)
    {
        Assert.Equal(expected, FfmpegArguments.SplitCommandLine(input));
    }
}
