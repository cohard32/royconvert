using RoyConvert.Core.Ffmpeg;

namespace RoyConvert.Core.Tests;

public class FfmpegProgressTests
{
    [Fact]
    public void ParsesDurationLine()
    {
        var duration = FfmpegProgress.ParseDuration("  Duration: 01:02:03.50, start: 0.000000, bitrate: 1205 kb/s");
        Assert.Equal(new TimeSpan(0, 1, 2, 3, 500), duration);
    }

    [Theory]
    [InlineData("  Duration: N/A, start: 0.000000, bitrate: N/A")]
    [InlineData("Stream #0:0: Video: h264")]
    public void IgnoresLinesWithoutDuration(string line)
    {
        Assert.Null(FfmpegProgress.ParseDuration(line));
    }

    [Theory]
    [InlineData("out_time_us=2500000", 2.5)]
    [InlineData("out_time_ms=2500000", 2.5)]
    public void ParsesOutTimeInMicroseconds(string line, double seconds)
    {
        Assert.Equal(TimeSpan.FromSeconds(seconds), FfmpegProgress.ParseOutTime(line));
    }

    [Theory]
    [InlineData("out_time_us=N/A")]
    [InlineData("out_time_us=-1")]
    [InlineData("out_time=00:00:02.500000")]
    [InlineData("frame=10")]
    public void IgnoresOtherProgressLines(string line)
    {
        Assert.Null(FfmpegProgress.ParseOutTime(line));
    }

    [Fact]
    public void Fraction_IsClampedAndNullWithoutDuration()
    {
        Assert.Equal(0.5, FfmpegProgress.Fraction(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10)));
        Assert.Equal(1, FfmpegProgress.Fraction(TimeSpan.FromSeconds(11), TimeSpan.FromSeconds(10)));
        Assert.Null(FfmpegProgress.Fraction(TimeSpan.FromSeconds(5), null));
        Assert.Null(FfmpegProgress.Fraction(TimeSpan.FromSeconds(5), TimeSpan.Zero));
    }

    [Fact]
    public void DetectsEnd()
    {
        Assert.True(FfmpegProgress.IsEnd("progress=end"));
        Assert.False(FfmpegProgress.IsEnd("progress=continue"));
    }
}
