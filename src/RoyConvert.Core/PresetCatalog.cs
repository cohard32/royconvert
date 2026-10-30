namespace RoyConvert.Core;

/// <summary>Built-in presets. Names use "/" for submenus.</summary>
public static class PresetCatalog
{
    private static readonly OutputFormat[] VideoOutputs = [OutputFormat.Mp4, OutputFormat.Mkv, OutputFormat.Webm, OutputFormat.Avi];
    private static readonly OutputFormat[] AudioOutputs = [OutputFormat.Mp3, OutputFormat.Ogg, OutputFormat.Flac, OutputFormat.Wav, OutputFormat.Aac];
    private static readonly OutputFormat[] ImageOutputs = [OutputFormat.Png, OutputFormat.Jpg, OutputFormat.Webp, OutputFormat.Avif, OutputFormat.Ico];

    private static readonly IReadOnlySet<string> VideoSources = Union(Formats.VideoInputs, ["gif"]);
    private static readonly IReadOnlySet<string> AudioSources = Union(Formats.AudioInputs, Formats.VideoInputs);

    /// <summary>Image formats that PDF pages can be exported to.</summary>
    private static readonly OutputFormat[] PdfPageOutputs = [OutputFormat.Png, OutputFormat.Jpg, OutputFormat.Webp];

    public static IReadOnlyList<Preset> Default { get; } = CreateDefault();

    public static Preset? Find(IEnumerable<Preset> presets, string id) =>
        presets.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>Presets that accept every one of the given files.</summary>
    public static IEnumerable<Preset> ForFiles(IEnumerable<Preset> presets, IReadOnlyCollection<string> paths) =>
        presets.Where(p => paths.Count > 0 && paths.All(p.Accepts));

    private static List<Preset> CreateDefault()
    {
        var presets = new List<Preset>();
        var video = new ConversionSettings();
        var image = new ConversionSettings();

        foreach (var format in VideoOutputs)
        {
            presets.Add(Make(format, video, VideoSources));
        }

        presets.Add(new Preset("mp4-small", "MP4 (small)", OutputFormat.Mp4, VideoSources,
            video with { VideoQuality = 28, EncodingSpeed = EncodingSpeed.Faster, AudioBitrate = 128 },
            "{dir}/{name}-small"));
        presets.Add(Make(OutputFormat.Gif, video, Formats.VideoInputs));

        foreach (var format in AudioOutputs)
        {
            presets.Add(Make(format, video, AudioSources));
        }

        foreach (var format in ImageOutputs)
        {
            var sources = PdfPageOutputs.Contains(format) ? Union(Formats.ImageInputs, ["pdf"]) : Formats.ImageInputs;
            presets.Add(Make(format, image, sources));
        }

        presets.Add(Make(OutputFormat.Pdf, image, Union(Formats.OfficeInputs, Formats.ImageInputs)));
        presets.Add(new Preset("combine-pdf", "PDF (one file)", OutputFormat.Pdf, Union(Formats.ImageInputs, ["pdf"]), image,
            "{dir}/{name} (combined)", CombinesInputs: true));
        presets.Add(new Preset("docx", "Word (DOCX)", OutputFormat.Docx, Union([], ["pdf"]), image));
        presets.Add(new Preset("split-pdf", "PDF (split pages)", OutputFormat.Pdf, Union([], ["pdf"]), image,
            "{dir}/{name}-page", SplitsPages: true));
        // Smaller than the image presets' quality: the point is the file size, and 150 dpi still prints well.
        presets.Add(new Preset("compress-pdf", "PDF (compressed)", OutputFormat.Pdf, Union([], ["pdf"]), image with { ImageQuality = 75 },
            "{dir}/{name} (compressed)", CompressesPdf: true));

        foreach (var format in (OutputFormat[])[OutputFormat.Mp4, OutputFormat.Mkv])
        {
            var label = Formats.Extension(format).ToUpperInvariant();
            presets.Add(new Preset("dvd/" + Formats.Extension(format), "DVD/" + label, format, new HashSet<string>(), video,
                ReadsDvd: true));
        }

        AddGroup(presets, "scale75", "Scale 75%", video with { Scale = Scale.ByFactor(0.75) }, image with { Scale = Scale.ByFactor(0.75) }, "-75%");
        AddGroup(presets, "scale50", "Scale 50%", video with { Scale = Scale.ByFactor(0.5) }, image with { Scale = Scale.ByFactor(0.5) }, "-50%");
        AddGroup(presets, "scale25", "Scale 25%", video with { Scale = Scale.ByFactor(0.25) }, image with { Scale = Scale.ByFactor(0.25) }, "-25%");
        AddGroup(presets, "720p", "720p", video with { Scale = Scale.ToHeight(720) }, null, " (720p)");
        AddGroup(presets, "1080p", "1080p", video with { Scale = Scale.ToHeight(1080) }, null, " (1080p)");
        AddGroup(presets, "rotate-left", "Rotate left", video with { Rotation = Rotation.Left }, image with { Rotation = Rotation.Left }, " (rotated)");
        AddGroup(presets, "rotate-right", "Rotate right", video with { Rotation = Rotation.Right }, image with { Rotation = Rotation.Right }, " (rotated)");
        foreach (var format in ImageOutputs.Where(f => f != OutputFormat.Ico))
        {
            presets.Add(Make(format, image with { StripMetadata = true }, Formats.ImageInputs, "strip-metadata", "Remove metadata",
                "{dir}/{name} (no metadata)"));
        }

        // Into the same format the picture is copied, not encoded (see FfmpegArguments.CanCopyVideo).
        foreach (var format in VideoOutputs)
        {
            presets.Add(Make(format, video with { EnableAudio = false }, Formats.VideoInputs, "mute", "Remove sound",
                "{dir}/{name} (no sound)"));
        }

        return presets;
    }

    /// <summary>Adds a submenu with the same transform for every video (and optionally image) output.</summary>
    private static void AddGroup(List<Preset> presets, string id, string name, ConversionSettings video, ConversionSettings? image, string suffix)
    {
        var template = "{dir}/{name}" + suffix;
        foreach (var format in VideoOutputs.Append(OutputFormat.Gif))
        {
            var sources = format == OutputFormat.Gif ? Formats.VideoInputs : VideoSources;
            presets.Add(Make(format, video, sources, id, name, template));
        }

        if (image is null)
        {
            return;
        }

        foreach (var format in ImageOutputs.Where(f => f != OutputFormat.Ico))
        {
            presets.Add(Make(format, image, Formats.ImageInputs, id, name, template));
        }

        if (image.Rotation != Rotation.None)
        {
            // Rotating a PDF turns its pages; nothing is rendered.
            presets.Add(Make(OutputFormat.Pdf, image, Union([], ["pdf"]), id, name, template));
        }
    }

    private static Preset Make(OutputFormat format, ConversionSettings settings, IReadOnlySet<string> inputs,
        string? groupId = null, string? groupName = null, string template = OutputPath.DefaultTemplate)
    {
        var formatId = Formats.Extension(format);
        var label = formatId.ToUpperInvariant();
        // A file never needs converting to its own format unless something else changes.
        var sources = groupId is null ? Without(inputs, formatId) : inputs;
        return new Preset(
            groupId is null ? formatId : $"{groupId}/{formatId}",
            groupName is null ? label : $"{groupName}/{label}",
            format,
            sources,
            settings,
            template);
    }

    private static IReadOnlySet<string> Union(IEnumerable<string> a, IEnumerable<string> b) =>
        new HashSet<string>(a.Concat(b), StringComparer.OrdinalIgnoreCase);

    private static IReadOnlySet<string> Without(IReadOnlySet<string> set, string extension)
    {
        var result = new HashSet<string>(set, StringComparer.OrdinalIgnoreCase);
        result.Remove(extension);
        if (extension == "jpg")
        {
            result.ExceptWith(["jpeg", "jfif"]);
        }

        return result;
    }
}
