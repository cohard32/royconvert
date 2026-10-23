namespace RoyConvert.Core.Tests;

public class OutputPathTests
{
    private static readonly string Dir = Path.Combine(Path.GetTempPath(), "rc-out");
    private static readonly string Input = Path.Combine(Dir, "clip.MOV");

    [Fact]
    public void DefaultTemplate_ReplacesExtension()
    {
        var path = OutputPath.Build(Input, OutputPath.DefaultTemplate, OutputFormat.Mp4, _ => false);
        Assert.Equal(Path.Combine(Dir, "clip.mp4"), path);
    }

    [Fact]
    public void Template_SupportsSuffixAndOriginalExtension()
    {
        var path = OutputPath.Build(Input, "{dir}/{name}-{ext} (720p)", OutputFormat.Webm, _ => false);
        Assert.Equal(Path.Combine(Dir, "clip-mov (720p).webm"), path);
    }

    [Fact]
    public void ExistingFiles_GetANumberedName()
    {
        var taken = new HashSet<string> { Path.Combine(Dir, "clip.mp4"), Path.Combine(Dir, "clip (1).mp4") };
        var path = OutputPath.Build(Input, OutputPath.DefaultTemplate, OutputFormat.Mp4, taken.Contains);
        Assert.Equal(Path.Combine(Dir, "clip (2).mp4"), path);
    }

    [Fact]
    public void SameFormat_NeverOverwritesInput()
    {
        var input = Path.Combine(Dir, "photo.png");
        var path = OutputPath.Build(input, OutputPath.DefaultTemplate, OutputFormat.Png, _ => false);
        Assert.Equal(Path.Combine(Dir, "photo (1).png"), path);
    }

    [Fact]
    public void JpgFormat_UsesJpgExtension()
    {
        Assert.EndsWith(".jpg", OutputPath.Build(Input, OutputPath.DefaultTemplate, OutputFormat.Jpg, _ => false));
    }
}
