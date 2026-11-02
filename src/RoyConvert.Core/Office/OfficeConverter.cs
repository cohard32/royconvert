using System.Diagnostics;

namespace RoyConvert.Core.Office;

/// <summary>Exports office documents to PDF, and spreadsheets to CSV, with LibreOffice in headless mode.</summary>
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

    /// <summary>
    /// CSV filter options: ';' between fields (Turkish and other comma-decimal locales expect it), '"' around text,
    /// UTF-8, cells as shown (dates and decimals as formatted, formulas as their results), every sheet (-1).
    /// </summary>
    internal const string CsvFilter = "csv:Text - txt - csv (StarCalc):59,34,76,1,,0,false,true,false,false,false,-1";

    /// <summary>After Microsoft Office for its own formats, before it for OpenDocument files; the only one for CSV.</summary>
    public int? Priority(string input, OutputFormat output) => output switch
    {
        OutputFormat.Pdf when Formats.OfficeInputs.Contains(Formats.ExtensionOf(input)) => 1,
        OutputFormat.Csv when Formats.SpreadsheetInputs.Contains(Formats.ExtensionOf(input)) => 1,
        _ => null,
    };

    public async Task ConvertAsync(string input, string output, Preset preset, IProgress<double?> progress, CancellationToken cancellationToken)
    {
        if (preset.Output is not (OutputFormat.Pdf or OutputFormat.Csv))
        {
            throw new ArgumentOutOfRangeException(nameof(preset), preset.Output, "LibreOffice only exports PDF and CSV.");
        }

        progress.Report(null);

        // soffice always names the result after the input and refuses to run twice with the same profile,
        // so every job gets its own output folder and profile.
        var work = Path.Combine(Path.GetTempPath(), "royconvert-office-" + Guid.NewGuid().ToString("N"));
        var outDir = Path.Combine(work, "out");
        Directory.CreateDirectory(outDir);
        try
        {
            await RunAsync(input, work, outDir, preset.Output == OutputFormat.Csv ? CsvFilter : "pdf", cancellationToken);
            if (preset.Output == OutputFormat.Csv)
            {
                MoveSheets(input, outDir, output);
                progress.Report(1);
                return;
            }

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

    /// <param name="convertTo">"pdf", or a filter with options such as <see cref="CsvFilter"/>.</param>
    public static IReadOnlyList<string> Arguments(string input, string profileDir, string outDir, string convertTo = "pdf") =>
    [
        "-env:UserInstallation=" + new Uri(Path.GetFullPath(profileDir)).AbsoluteUri,
        "--headless",
        "--norestore",
        "--nolockcheck",
        "--convert-to", convertTo,
        "--outdir", outDir,
        Path.GetFullPath(input),
    ];

    /// <summary>
    /// soffice writes "name-Sheet.csv" for every sheet. One sheet becomes <paramref name="output"/> itself, several
    /// become "output-Sheet.csv" next to it (numbered if taken). Each gets a UTF-8 byte order mark: without it Excel
    /// reads the file in the ANSI code page and Turkish letters come out wrong.
    /// </summary>
    internal static IReadOnlyList<string> MoveSheets(string input, string outDir, string output)
    {
        var sheets = Directory.GetFiles(outDir, "*.csv").Order(StringComparer.Ordinal).ToList();
        if (sheets.Count == 0)
        {
            throw new ConversionException("LibreOffice did not create a CSV. The file may be damaged or password protected.");
        }

        var prefix = Path.GetFileNameWithoutExtension(input) + "-";
        var folder = Path.GetDirectoryName(Path.GetFullPath(output))!;
        var stem = Path.GetFileNameWithoutExtension(output);
        var written = new List<string>(sheets.Count);
        try
        {
            foreach (var sheet in sheets)
            {
                var name = Path.GetFileNameWithoutExtension(sheet);
                var sheetName = name.StartsWith(prefix, StringComparison.Ordinal) ? name[prefix.Length..] : name;
                var target = sheets.Count == 1 ? output : OutputPath.Beside(Path.Combine(folder, stem + ".csv"), "-" + sheetName, "csv");
                WriteWithBom(sheet, target);
                written.Add(target);
            }
        }
        catch
        {
            written.ForEach(path => File.Delete(path));
            throw;
        }

        return written;
    }

    private static void WriteWithBom(string source, string target)
    {
        var bytes = File.ReadAllBytes(source);
        var hasBom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
        using var file = new FileStream(target, FileMode.CreateNew);
        if (!hasBom)
        {
            file.Write([0xEF, 0xBB, 0xBF]);
        }

        file.Write(bytes);
    }

    private async Task RunAsync(string input, string work, string outDir, string convertTo, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(sofficePath)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var arg in Arguments(input, Path.Combine(work, "profile"), outDir, convertTo))
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
