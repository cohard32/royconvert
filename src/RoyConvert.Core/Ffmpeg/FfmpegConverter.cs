using System.Diagnostics;

namespace RoyConvert.Core.Ffmpeg;

public sealed class FfmpegConverter(string ffmpegPath) : IConverter
{
    private const int ErrorTailLines = 20;

    public Task ConvertAsync(string input, string output, Preset preset, IProgress<double?> progress, CancellationToken cancellationToken)
    {
        var args = preset.ReadsDvd
            ? FfmpegArguments.BuildDvd(Dvd.DvdDisc.Open(input) ?? throw new ConversionException("No DVD video was found there."),
                output, preset.Output, preset.Settings)
            : FfmpegArguments.Build(input, output, preset.Output, preset.Settings);
        return RunAsync(args, output, null, progress, cancellationToken);
    }

    /// <summary>How long the video or sound is, as ffmpeg reads it from the file; null if it cannot tell.</summary>
    public async Task<TimeSpan?> ProbeDurationAsync(string input, CancellationToken cancellationToken = default)
    {
        var startInfo = new ProcessStartInfo(ffmpegPath)
        {
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        // No output file: ffmpeg prints the input's details and exits with an error, which is expected here.
        foreach (var arg in (string[])["-hide_banner", "-nostdin", "-i", input])
        {
            startInfo.ArgumentList.Add(arg);
        }

        using var process = Process.Start(startInfo)
            ?? throw new ConversionException($"ffmpeg could not be started ({ffmpegPath}).");
        TimeSpan? duration = null;
        while (await process.StandardError.ReadLineAsync(cancellationToken) is { } line)
        {
            duration ??= FfmpegProgress.ParseDuration(line);
        }

        await process.WaitForExitAsync(cancellationToken);
        return duration;
    }

    /// <summary>
    /// Runs ffmpeg with <paramref name="args"/>, which write <paramref name="output"/>. Progress is measured against
    /// <paramref name="duration"/>, or the input's whole length when null.
    /// </summary>
    public async Task RunAsync(IReadOnlyList<string> args, string output, TimeSpan? duration, IProgress<double?> progress,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(ffmpegPath)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        using var process = new Process { StartInfo = startInfo };
        try
        {
            process.Start();
        }
        catch (Exception e)
        {
            throw new ConversionException($"ffmpeg could not be started ({ffmpegPath}).", e);
        }

        // Written by the stderr reader, read by the stdout reader.
        long durationTicks = duration?.Ticks ?? 0;
        var errorTail = new Queue<string>();

        var stderrTask = Task.Run(async () =>
        {
            while (await process.StandardError.ReadLineAsync(cancellationToken) is { } line)
            {
                if (Interlocked.Read(ref durationTicks) == 0 && FfmpegProgress.ParseDuration(line) is { } inputDuration)
                {
                    Interlocked.Exchange(ref durationTicks, inputDuration.Ticks);
                }

                lock (errorTail)
                {
                    errorTail.Enqueue(line);
                    if (errorTail.Count > ErrorTailLines)
                    {
                        errorTail.Dequeue();
                    }
                }
            }
        }, cancellationToken);

        var stdoutTask = Task.Run(async () =>
        {
            while (await process.StandardOutput.ReadLineAsync(cancellationToken) is { } line)
            {
                if (FfmpegProgress.ParseOutTime(line) is { } position)
                {
                    var ticks = Interlocked.Read(ref durationTicks);
                    progress.Report(FfmpegProgress.Fraction(position, ticks > 0 ? TimeSpan.FromTicks(ticks) : null));
                }
                else if (FfmpegProgress.IsEnd(line))
                {
                    progress.Report(1);
                }
            }
        }, cancellationToken);

        try
        {
            await process.WaitForExitAsync(cancellationToken);
            await Task.WhenAll(stderrTask, stdoutTask);
        }
        catch (OperationCanceledException)
        {
            KillQuietly(process);
            DeleteQuietly(output);
            throw;
        }

        if (process.ExitCode != 0)
        {
            DeleteQuietly(output);
            string details;
            lock (errorTail)
            {
                details = string.Join(Environment.NewLine, errorTail);
            }

            throw new ConversionException($"ffmpeg exited with code {process.ExitCode}.{Environment.NewLine}{details}");
        }
    }

    private static void KillQuietly(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit(5000);
        }
        catch (InvalidOperationException)
        {
            // Already exited.
        }
    }

    private static void DeleteQuietly(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
