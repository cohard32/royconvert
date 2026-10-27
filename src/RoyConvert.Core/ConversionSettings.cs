namespace RoyConvert.Core;

/// <summary>x264/x265 preset names, from fastest to slowest.</summary>
public enum EncodingSpeed
{
    UltraFast,
    SuperFast,
    VeryFast,
    Faster,
    Fast,
    Medium,
    Slow,
    Slower,
    VerySlow,
}

/// <summary>Clockwise rotation in degrees.</summary>
public enum Rotation
{
    None = 0,
    Right = 90,
    UpsideDown = 180,
    Left = 270,
}

/// <summary>Either a relative factor (0.75) or a fixed output height (720). Width keeps the aspect ratio.</summary>
public readonly record struct Scale(double? Factor, int? Height)
{
    public static readonly Scale Original = new(null, null);

    public static Scale ByFactor(double factor) => new(factor, null);

    public static Scale ToHeight(int height) => new(null, height);

    public bool IsOriginal => Factor is null or 1.0 && Height is null;
}

public sealed record ConversionSettings
{
    /// <summary>Video quality as CRF (lower is better). Mapped per codec.</summary>
    public int VideoQuality { get; init; } = 23;

    public EncodingSpeed EncodingSpeed { get; init; } = EncodingSpeed.Medium;

    public Scale Scale { get; init; } = Scale.Original;

    public Rotation Rotation { get; init; } = Rotation.None;

    public bool EnableAudio { get; init; } = true;

    /// <summary>Audio bitrate in kbit/s for lossy codecs.</summary>
    public int AudioBitrate { get; init; } = 192;

    /// <summary>Output channel count; null keeps the source layout.</summary>
    public int? AudioChannels { get; init; }

    public int GifFramesPerSecond { get; init; } = 15;

    /// <summary>Lossy image quality, 1-100.</summary>
    public int ImageQuality { get; init; } = 90;

    /// <summary>Images larger than this (on either side) are shrunk to fit. Null disables the limit.</summary>
    public int? ImageMaxSize { get; init; }

    /// <summary>Resolution PDF pages are rendered at when converted to images.</summary>
    public int PdfDpi { get; init; } = 150;

    /// <summary>Extra ffmpeg output arguments appended after the generated ones.</summary>
    public string? CustomFfmpegArguments { get; init; }
}
