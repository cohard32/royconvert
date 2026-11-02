using ImageMagick;

namespace RoyConvert.Core.Tests;

public sealed class ImageResizeTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("royconvert-resize-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string Photo(string name, uint width = 1200, uint height = 800, MagickFormat? format = null)
    {
        var path = Path.Combine(_dir, name);
        using var image = new MagickImage(MagickColors.Orange, width, height);
        if (format is { } f)
        {
            image.Write(path, f);
        }
        else
        {
            image.Write(path);
        }

        return path;
    }

    [Theory]
    [InlineData("1920", 1920)]
    [InlineData(" 800px ", 800)]
    [InlineData("100000", 100000)]
    [InlineData("0", null)]
    [InlineData("-5", null)]
    [InlineData("12.5", null)]
    [InlineData("100001", null)]
    [InlineData("abc", null)]
    [InlineData("", null)]
    public void ParsePixels(string text, int? expected) => Assert.Equal(expected, ImageResize.ParsePixels(text));

    [Theory]
    [InlineData("50", 50.0)]
    [InlineData("50%", 50.0)]
    [InlineData("%25", 25.0)]
    [InlineData("12,5", 12.5)]
    [InlineData("12.5", 12.5)]
    [InlineData("100", null)]
    [InlineData("0", null)]
    [InlineData("150", null)]
    [InlineData("yarım", null)]
    public void ParsePercent(string text, double? expected) => Assert.Equal(expected, ImageResize.ParsePercent(text));

    [Theory]
    [InlineData(800, null, null, 800, 533)]
    [InlineData(null, 400, null, 600, 400)]
    [InlineData(600, 600, null, 600, 400)]    // fits inside the box
    [InlineData(1000, 200, null, 300, 200)]
    [InlineData(null, null, 25.0, 300, 200)]
    [InlineData(5000, null, null, 1200, 800)] // never enlarged
    [InlineData(1, 1, null, 1, 1)]           // never zero
    public void TargetSize_KeepsProportions_NeverEnlarges(int? width, int? height, double? percent, uint expectedWidth, uint expectedHeight)
    {
        Assert.Equal((expectedWidth, expectedHeight), ImageResize.TargetSize(1200, 800, new ResizeOptions(width, height, percent)));
    }

    [Fact]
    public void Inputs_ArePictures_NotIconsOrDrawings()
    {
        Assert.Contains("heic", ImageResize.Inputs);
        Assert.Contains("gif", ImageResize.Inputs);
        Assert.DoesNotContain("svg", ImageResize.Inputs);
        Assert.DoesNotContain("ico", ImageResize.Inputs);
        Assert.DoesNotContain("pdf", ImageResize.Inputs);
    }

    [Fact]
    public void OutputPath_KeepsTheFormat_OrJpegForOthers()
    {
        Assert.Equal(Path.Combine(_dir, "a (k).png"), ImageResize.OutputPathFor(Path.Combine(_dir, "a.png"), " (k)", _ => false));
        Assert.Equal(Path.Combine(_dir, "a (k).jpg"), ImageResize.OutputPathFor(Path.Combine(_dir, "a.heic"), " (k)", _ => false));
        Assert.Equal(Path.Combine(_dir, "a (k).jpg"), ImageResize.OutputPathFor(Path.Combine(_dir, "a.psd"), " (k)", _ => false));
    }

    [Fact]
    public void Apply_WritesASmallerCopy_InputUntouched()
    {
        var input = Photo("foto.jpg");
        var before = File.ReadAllBytes(input);
        var output = ImageResize.OutputPathFor(input, " (k)");

        ImageResize.Apply(input, output, new ResizeOptions(Width: 600), quality: 85);

        using var result = new MagickImage(output);
        Assert.Equal((600u, 400u), (result.Width, result.Height));
        Assert.Equal(MagickFormat.Jpeg, result.Format);
        Assert.Equal(before, File.ReadAllBytes(input));
    }

    [Fact]
    public void Apply_TurnsPhotosUpright_ThenResizes()
    {
        var input = Path.Combine(_dir, "dik.jpg");
        using (var image = new MagickImage(MagickColors.Orange, 1200, 800))
        {
            var exif = new ExifProfile();
            exif.SetValue(ExifTag.Orientation, (ushort)6); // shown turned 90°, so 800 × 1200
            image.SetProfile(exif);
            image.Orientation = Orientation.RightTop; // what Magick writes as the EXIF orientation
            image.Write(input);
        }

        Assert.Equal((800u, 1200u), ImageResize.ReadSize(input));
        var output = Path.Combine(_dir, "dik (k).jpg");
        ImageResize.Apply(input, output, new ResizeOptions(Height: 600), quality: 85);

        using var result = new MagickImage(output);
        Assert.Equal((400u, 600u), (result.Width, result.Height));
        Assert.Equal(Orientation.TopLeft, result.Orientation);
    }

    [Fact]
    public void Apply_PngKeepsTransparency()
    {
        var input = Path.Combine(_dir, "seffaf.png");
        using (var image = new MagickImage(MagickColors.Transparent, 400, 400))
        {
            image.Write(input);
        }

        var output = Path.Combine(_dir, "seffaf (k).png");
        ImageResize.Apply(input, output, new ResizeOptions(Percent: 50), quality: 85);

        using var result = new MagickImage(output);
        Assert.Equal((200u, 200u), (result.Width, result.Height));
        Assert.True(result.HasAlpha);
    }

    [Fact]
    public void Apply_AnimatedGif_KeepsEveryFrame()
    {
        var input = Path.Combine(_dir, "hareket.gif");
        using (var frames = new MagickImageCollection())
        {
            frames.Add(new MagickImage(MagickColors.Red, 400, 200));
            frames.Add(new MagickImage(MagickColors.Blue, 400, 200));
            frames.Add(new MagickImage(MagickColors.Green, 400, 200));
            frames.Write(input, MagickFormat.Gif);
        }

        var output = Path.Combine(_dir, "hareket (k).gif");
        ImageResize.Apply(input, output, new ResizeOptions(Width: 100), quality: 85);

        using var result = new MagickImageCollection(output);
        Assert.Equal(3, result.Count);
        result.Coalesce();
        Assert.All(result, frame => Assert.Equal((100u, 50u), (frame.Width, frame.Height)));
    }

    [Fact]
    public void Apply_UnreadableFile_IsAConversionError_NothingLeft()
    {
        var input = Path.Combine(_dir, "bozuk.jpg");
        File.WriteAllText(input, "resim değil");
        var output = Path.Combine(_dir, "bozuk (k).jpg");

        Assert.Throws<ConversionException>(() => ImageResize.Apply(input, output, new ResizeOptions(Width: 10), quality: 85));
        Assert.False(File.Exists(output));
        Assert.Throws<ConversionException>(() => ImageResize.ReadSize(input));
    }
}
