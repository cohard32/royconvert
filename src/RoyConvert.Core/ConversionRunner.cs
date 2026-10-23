using RoyConvert.Core.Ffmpeg;
using RoyConvert.Core.Imaging;

namespace RoyConvert.Core;

/// <summary>Runs conversion jobs with bounded parallelism.</summary>
public sealed class ConversionRunner(Func<Preset, IConverter> converterFor)
{
    /// <summary>Half the logical cores: ffmpeg encoders are multi-threaded themselves.</summary>
    public static int DefaultParallelism => Math.Max(1, Environment.ProcessorCount / 2);

    /// <summary>Uses ffmpeg for audio/video and Magick.NET for images.</summary>
    public static ConversionRunner CreateDefault(string? ffmpegPath)
    {
        var image = new ImageConverter();
        IConverter? ffmpeg = ffmpegPath is null ? null : new FfmpegConverter(ffmpegPath);
        return new ConversionRunner(preset => Formats.KindOf(preset.Output) == MediaKind.Image
            ? image
            : ffmpeg ?? throw new ConversionException("ffmpeg was not found."));
    }

    /// <summary>Completes when every job has finished, failed or been canceled. Never throws for individual job failures.</summary>
    public async Task RunAsync(IReadOnlyList<ConversionJob> jobs, int maxParallelism, CancellationToken cancellationToken = default)
    {
        var options = new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, maxParallelism) };
        // Jobs observe the token themselves and finish as Canceled, so it is not passed to ForEachAsync.
        await Parallel.ForEachAsync(jobs, options, (job, _) => new ValueTask(job.RunAsync(converterFor, ReserveOutput, cancellationToken)));
    }

    // Paths handed to running jobs: their files may not exist yet, but must not be chosen twice.
    private readonly HashSet<string> _reserved = new(StringComparer.OrdinalIgnoreCase);

    private string ReserveOutput(ConversionJob job)
    {
        lock (_reserved)
        {
            var path = OutputPath.Build(job.Input, job.Preset.FileNameTemplate, job.Preset.Output,
                candidate => _reserved.Contains(candidate) || File.Exists(candidate));
            _reserved.Add(path);
            return path;
        }
    }
}
