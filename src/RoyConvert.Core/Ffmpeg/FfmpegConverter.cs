using System.Diagnostics;

namespace RoyConvert.Core.Ffmpeg;

public sealed class FfmpegConverter(string ffmpegPath) : IConverter
{
    private const int ErrorTailLines = 20;

    public async Task ConvertAsync(string input, string output, Preset preset, IProgress<double?> progress, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(ffmpegPath)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var arg in FfmpegArguments.Build(input, output, preset.Output, preset.Settings))
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
        long durationTicks = 0;
        var errorTail = new Queue<string>();

        var stderrTask = Task.Run(async () =>
        {
            while (await process.StandardError.ReadLineAsync(cancellationToken) is { } line)
            {
                if (Interlocked.Read(ref durationTicks) == 0 && FfmpegProgress.ParseDuration(line) is { } duration)
                {
                    Interlocked.Exchange(ref durationTicks, duration.Ticks);
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
