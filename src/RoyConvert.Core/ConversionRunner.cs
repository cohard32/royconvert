using System.Threading.Channels;
using RoyConvert.Core.Ffmpeg;
using RoyConvert.Core.Imaging;
using RoyConvert.Core.Office;

namespace RoyConvert.Core;

/// <summary>A long-lived queue that runs conversion jobs with bounded parallelism. Jobs can be added at any time.</summary>
public sealed class ConversionRunner : IDisposable
{
    private readonly Func<Preset, IConverter> _converterFor;
    private readonly Channel<ConversionJob> _queue = Channel.CreateUnbounded<ConversionJob>();
    private readonly Task[] _workers;

    // Paths handed to running jobs: their files may not exist yet, but must not be chosen twice.
    private readonly HashSet<string> _reserved = new(StringComparer.OrdinalIgnoreCase);

    private int _pending;

    public ConversionRunner(Func<Preset, IConverter> converterFor, int maxParallelism)
    {
        _converterFor = converterFor;
        _workers = Enumerable.Range(0, Math.Max(1, maxParallelism)).Select(_ => Task.Run(WorkAsync)).ToArray();
    }

    /// <summary>Half the logical cores: ffmpeg encoders are multi-threaded themselves.</summary>
    public static int DefaultParallelism => Math.Max(1, Environment.ProcessorCount / 2);

    /// <summary>Raised on a worker thread when the last queued job finishes.</summary>
    public event Action? Idle;

    /// <summary>
    /// Uses ffmpeg for audio/video, Magick.NET for images (also PDF pages and image → PDF) and Microsoft Office
    /// (<paramref name="microsoftOffice"/>, see <see cref="MicrosoftOfficeConverter.FindInstalled"/>) or LibreOffice for
    /// office documents → PDF.
    /// </summary>
    public static ConversionRunner CreateDefault(string? ffmpegPath, int? maxParallelism = null, string? libreOfficePath = null,
        IDocumentConverter? microsoftOffice = null)
    {
        var image = new ImageConverter();
        IConverter? ffmpeg = ffmpegPath is null ? null : new FfmpegConverter(ffmpegPath);
        var documents = new List<IDocumentConverter>();
        if (microsoftOffice is not null)
        {
            documents.Add(microsoftOffice);
        }

        if (libreOfficePath is not null)
        {
            documents.Add(new OfficeConverter(libreOfficePath));
        }

        var pdf = new PdfConverter(image, documents);
        return new ConversionRunner(
            preset => Formats.KindOf(preset.Output) switch
            {
                MediaKind.Image => image,
                MediaKind.Document => pdf,
                _ => ffmpeg ?? throw new ConversionException("ffmpeg was not found."),
            },
            maxParallelism ?? DefaultParallelism);
    }

    public void Enqueue(ConversionJob job)
    {
        Interlocked.Increment(ref _pending);
        if (!_queue.Writer.TryWrite(job))
        {
            Interlocked.Decrement(ref _pending);
            throw new ObjectDisposedException(nameof(ConversionRunner));
        }
    }

    /// <summary>Enqueues the jobs and completes when all of them have finished, failed or been canceled.</summary>
    public Task RunAsync(IEnumerable<ConversionJob> jobs)
    {
        var list = jobs.ToList();
        list.ForEach(Enqueue);
        return Task.WhenAll(list.Select(j => j.Completion));
    }

    /// <summary>Stops accepting jobs. Queued jobs still run; use <see cref="ConversionJob.Cancel"/> to stop them.</summary>
    public void Dispose() => _queue.Writer.TryComplete();

    private async Task WorkAsync()
    {
        await foreach (var job in _queue.Reader.ReadAllAsync())
        {
            await job.RunAsync(_converterFor, ReserveOutput);
            if (Interlocked.Decrement(ref _pending) == 0)
            {
                Idle?.Invoke();
            }
        }
    }

    private string ReserveOutput(ConversionJob job)
    {
        lock (_reserved)
        {
            var input = job.Preset.ReadsDvd && Dvd.DvdDisc.Open(job.Input) is { } disc ? disc.NamingPath : job.Input;
            var path = OutputPath.Build(input, job.Preset.FileNameTemplate, job.Preset.Output,
                candidate => _reserved.Contains(candidate) || File.Exists(candidate));
            _reserved.Add(path);
            return path;
        }
    }
}
