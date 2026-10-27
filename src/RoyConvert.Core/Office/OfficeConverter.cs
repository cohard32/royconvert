using System.Diagnostics;

namespace RoyConvert.Core.Office;

/// <summary>Exports office documents to PDF with LibreOffice in headless mode.</summary>
/// <param name="timeout">
/// soffice can hang instead of failing (e.g. on an export it does not support), so it is killed after this long.
/// Defaults to <see cref="DefaultTimeout"/>.
/// </param>
public sealed class OfficeConverter(string sofficePath, TimeSpan? timeout = null) : IDocumentConverter
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(10);

    private const int ErrorTailLines = 20;

    private readonly TimeSpan _timeout = timeout ?? DefaultTimeout;

    public string Name => "LibreOffice";

    /// <summary>After Microsoft Office for its own formats, before it for OpenDocument files.</summary>
    public int? Priority(string input) => Formats.OfficeInputs.Contains(Formats.ExtensionOf(input)) ? 1 : null;

    public async Task ConvertAsync(string input, string output, Preset preset, IProgress<double?> progress, CancellationToken cancellationToken)
    {
        if (preset.Output != OutputFormat.Pdf)
        {
            throw new ArgumentOutOfRangeException(nameof(preset), preset.Output, "LibreOffice only exports PDF.");
        }

        progress.Report(null);

        // soffice always names the result after the input and refuses to run twice with the same profile,
        // so every job gets its own output folder and profile.
        var work = Path.Combine(Path.GetTempPath(), "royconvert-office-" + Guid.NewGuid().ToString("N"));
        var outDir = Path.Combine(work, "out");
        Directory.CreateDirectory(outDir);
        try
        {
            await RunAsync(input, work, outDir, cancellationToken);

            var result = Path.Combine(outDir, Path.GetFileNameWithoutExtension(input) + ".pdf");
            if (!File.Exists(result))
            {
                throw new ConversionException("LibreOffice did not create a PDF. The file may be damaged or password protected.");
            }

            File.Move(result, output);
        }
        finally
        {
            DeleteQuietly(work);
        }

        progress.Report(1);
    }

    public static IReadOnlyList<string> Arguments(string input, string profileDir, string outDir) =>
    [
        "-env:UserInstallation=" + new Uri(Path.GetFullPath(profileDir)).AbsoluteUri,
        "--headless",
        "--norestore",
        "--nolockcheck",
        "--convert-to", "pdf",
        "--outdir", outDir,
        Path.GetFullPath(input),
    ];

    private async Task RunAsync(string input, string work, string outDir, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(sofficePath)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var arg in Arguments(input, Path.Combine(work, "profile"), outDir))
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
            throw new ConversionException($"LibreOffice could not be started ({sofficePath}).", e);
        }

        using var timeoutOrCancel = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutOrCancel.CancelAfter(_timeout);
        var token = timeoutOrCancel.Token;

        var outputTail = new Queue<string>();
        var readers = new[] { process.StandardOutput, process.StandardError }.Select(reader => Task.Run(async () =>
        {
            while (await reader.ReadLineAsync(token) is { } line)
            {
                lock (outputTail)
                {
                    outputTail.Enqueue(line);
                    if (outputTail.Count > ErrorTailLines)
                    {
                        outputTail.Dequeue();
                    }
                }
            }
        }, token)).ToArray();

        try
        {
            await process.WaitForExitAsync(token);
            await Task.WhenAll(readers);
        }
        catch (OperationCanceledException)
        {
            KillQuietly(process);
            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            throw new ConversionException($"LibreOffice did not finish within {_timeout.TotalMinutes:0.#} minutes.");
        }

        if (process.ExitCode != 0)
        {
            string details;
            lock (outputTail)
            {
                details = string.Join(Environment.NewLine, outputTail);
            }

            throw new ConversionException($"LibreOffice exited with code {process.ExitCode}.{Environment.NewLine}{details}");
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

    private static void DeleteQuietly(string directory)
    {
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
