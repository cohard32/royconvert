using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace RoyConvert.Core.Office;

public enum OfficeApp
{
    Word,
    Excel,
    PowerPoint,
}

/// <summary>
/// Exports documents to PDF with the installed Microsoft Word, Excel or PowerPoint through COM automation, and
/// PDFs to editable Word documents (DOCX) with Word. The source is opened read-only with macros disabled and closed
/// without saving, so it is never changed.
/// </summary>
/// <param name="timeout">
/// A hidden dialog (e.g. an unexpected prompt) would block forever, so the Office process is killed after this long.
/// </param>
[SupportedOSPlatform("windows")]
public sealed class MicrosoftOfficeConverter(IReadOnlySet<OfficeApp> installed, TimeSpan? timeout = null) : IDocumentConverter
{
    // Opening a protected file with a wrong password fails at once instead of showing a password prompt;
    // files without a password ignore it.
    private const string WrongPassword = "royconvert-no-password";

    private static readonly IReadOnlyDictionary<string, OfficeApp> AppByExtension =
        new Dictionary<string, OfficeApp>(StringComparer.OrdinalIgnoreCase)
        {
            ["doc"] = OfficeApp.Word,
            ["docx"] = OfficeApp.Word,
            ["rtf"] = OfficeApp.Word,
            ["odt"] = OfficeApp.Word,
            ["xls"] = OfficeApp.Excel,
            ["xlsx"] = OfficeApp.Excel,
            ["ods"] = OfficeApp.Excel,
            ["ppt"] = OfficeApp.PowerPoint,
            ["pptx"] = OfficeApp.PowerPoint,
            ["odp"] = OfficeApp.PowerPoint,
        };

    // Office automation is not made for parallel use: one document at a time.
    private static readonly SemaphoreSlim Gate = new(1, 1);

    private readonly TimeSpan _timeout = timeout ?? OfficeConverter.DefaultTimeout;

    public string Name => "Microsoft Office";

    public IReadOnlySet<OfficeApp> Installed => installed;

    /// <summary>The Office apps registered for automation, or null when there are none.</summary>
    public static MicrosoftOfficeConverter? FindInstalled()
    {
        var apps = Enum.GetValues<OfficeApp>().Where(app => Type.GetTypeFromProgID(ProgId(app)) is not null).ToHashSet();
        return apps.Count == 0 ? null : new MicrosoftOfficeConverter(apps);
    }

    /// <summary>
    /// Office documents → PDF: first for Microsoft's own formats; for OpenDocument files after LibreOffice (priority 1),
    /// which writes them natively. PDF → DOCX: only Word can (it rebuilds an editable document from the PDF).
    /// </summary>
    public int? Priority(string input, OutputFormat output)
    {
        var extension = Formats.ExtensionOf(input);
        if (AppFor(extension, output) is not { } app || !installed.Contains(app))
        {
            return null;
        }

        return extension.StartsWith("od", StringComparison.Ordinal) ? 2 : 0;
    }

    public async Task ConvertAsync(string input, string output, Preset preset, IProgress<double?> progress, CancellationToken cancellationToken)
    {
        if (AppFor(Formats.ExtensionOf(input), preset.Output) is not { } app || !installed.Contains(app))
        {
            throw new ConversionException(preset.Output == OutputFormat.Docx
                ? "Converting PDF to Word needs Microsoft Word."
                : $"Microsoft Office cannot open {Path.GetFileName(input)}.");
        }

        progress.Report(null);
        await Gate.WaitAsync(cancellationToken);
        var work = Path.Combine(Path.GetTempPath(), "royconvert-msoffice-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(work);
            var result = Path.Combine(work, "out." + Formats.Extension(preset.Output));
            var session = new Session(app, Path.GetFullPath(input), result, preset.Output);

            // COM objects of Office live in a single-threaded apartment.
            var thread = new Thread(session.Run) { IsBackground = true, Name = "royconvert " + app };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();

            using var timeoutOrCancel = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutOrCancel.CancelAfter(_timeout);
            try
            {
                await session.Completion.WaitAsync(timeoutOrCancel.Token);
            }
            catch (OperationCanceledException)
            {
                session.Kill();
                if (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }

                throw new ConversionException($"Microsoft {app} did not finish within {_timeout.TotalMinutes:0.#} minutes.");
            }

            if (!File.Exists(result))
            {
                throw new ConversionException($"Microsoft {app} did not create the {Formats.Extension(preset.Output).ToUpperInvariant()} file.");
            }

            File.Move(result, output);
        }
        finally
        {
            Gate.Release();
            DeleteQuietly(work);
        }

        progress.Report(1);
    }

    private static string ProgId(OfficeApp app) => app + ".Application";

    private static OfficeApp? AppFor(string extension, OutputFormat output) => output switch
    {
        OutputFormat.Pdf when AppByExtension.TryGetValue(extension, out var app) => app,
        OutputFormat.Docx when extension == "pdf" => OfficeApp.Word,
        _ => null,
    };

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

    /// <summary>One export on its own STA thread; <see cref="Kill"/> ends the Office process it started.</summary>
    private sealed class Session(OfficeApp app, string input, string output, OutputFormat format)
    {
        private readonly TaskCompletionSource _done = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly object _lock = new();
        private int[] _started = [];
        private bool _killed;

        public Task Completion => _done.Task;

        private string ProcessName => app switch
        {
            OfficeApp.Word => "WINWORD",
            OfficeApp.Excel => "EXCEL",
            _ => "POWERPNT",
        };

        public void Run()
        {
            try
            {
                Export();
                _done.TrySetResult();
            }
            catch (Exception e)
            {
                var reason = e is COMException ? e.Message : e.GetType().Name + ": " + e.Message;
                var hint = format == OutputFormat.Docx
                    ? "Word's PDF import may not work on this computer"
                    : "It may be damaged or password protected";
                _done.TrySetException(new ConversionException(
                    $"Microsoft {app} could not convert the file. {hint}. ({reason.Trim()})", e));
            }
            finally
            {
                // Releases the remaining COM references so a PowerPoint we did not start is not kept alive by us.
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
        }

        /// <summary>Kills the Office process this session started; one that was already running is left alone.</summary>
        public void Kill()
        {
            int[] started;
            lock (_lock)
            {
                _killed = true;
                started = _started;
            }

            KillProcesses(started);
        }

        private void Export()
        {
            var before = ProcessIds();
            var type = Type.GetTypeFromProgID(ProgId(app)) ?? throw new COMException($"{ProgId(app)} is not registered.");
            dynamic office = Activator.CreateInstance(type) ?? throw new COMException($"{ProgId(app)} could not be started.");

            int[] started;
            bool killed;
            lock (_lock)
            {
                _started = started = ProcessIds().Except(before).ToArray();
                killed = _killed;
            }

            if (killed)
            {
                KillProcesses(started);
                return;
            }

            try
            {
                switch (app)
                {
                    case OfficeApp.Word:
                        ExportWord(office);
                        break;
                    case OfficeApp.Excel:
                        ExportExcel(office);
                        break;
                    default:
                        ExportPowerPoint(office);
                        break;
                }
            }
            finally
            {
                Quit(office, started.Length > 0);
                Marshal.FinalReleaseComObject(office);
            }
        }

        private void ExportWord(dynamic word)
        {
            word.Visible = false;
            word.DisplayAlerts = 0; // wdAlertsNone
            word.AutomationSecurity = 3; // msoAutomationSecurityForceDisable: macros never run
            dynamic document = word.Documents.Open(
                FileName: input, ConfirmConversions: false, ReadOnly: true, AddToRecentFiles: false,
                PasswordDocument: WrongPassword, Revert: false, Visible: false, NoEncodingDialog: true);
            try
            {
                if (format == OutputFormat.Docx)
                {
                    // A PDF input was rebuilt into an editable document by Word's PDF import ("reflow").
                    document.SaveAs2(FileName: output, FileFormat: 16 /* wdFormatXMLDocument */, AddToRecentFiles: false);
                }
                else
                {
                    document.ExportAsFixedFormat(
                        OutputFileName: output, ExportFormat: 17 /* wdExportFormatPDF */, OpenAfterExport: false,
                        OptimizeFor: 0 /* print quality */, CreateBookmarks: 1 /* from headings */);
                }
            }
            finally
            {
                document.Close(SaveChanges: 0 /* wdDoNotSaveChanges */);
                Marshal.FinalReleaseComObject(document);
            }
        }

        private void ExportExcel(dynamic excel)
        {
            excel.Visible = false;
            excel.DisplayAlerts = false;
            excel.AskToUpdateLinks = false;
            excel.AutomationSecurity = 3;
            dynamic workbook = excel.Workbooks.Open(
                Filename: input, UpdateLinks: 0, ReadOnly: true, Password: WrongPassword,
                IgnoreReadOnlyRecommended: true, Notify: false, AddToMru: false);
            try
            {
                // The workbook's export includes every sheet, like LibreOffice's.
                workbook.ExportAsFixedFormat(
                    Type: 0 /* xlTypePDF */, Filename: output, Quality: 0, IncludeDocProperties: true,
                    IgnorePrintAreas: false, OpenAfterPublish: false);
            }
            finally
            {
                workbook.Close(SaveChanges: false);
                Marshal.FinalReleaseComObject(workbook);
            }
        }

        private void ExportPowerPoint(dynamic powerPoint)
        {
            // PowerPoint has one instance per user, so this may be the one the user works in: only our
            // presentation is touched, and it is opened without a window.
            powerPoint.DisplayAlerts = 1; // ppAlertsNone
            powerPoint.AutomationSecurity = 3;
            dynamic presentation = powerPoint.Presentations.Open(
                FileName: input + "::" + WrongPassword + "::", ReadOnly: -1, Untitled: 0, WithWindow: 0);
            try
            {
                presentation.SaveCopyAs(FileName: output, FileFormat: 32 /* ppSaveAsPDF */);
            }
            finally
            {
                presentation.Close();
                Marshal.FinalReleaseComObject(presentation);
            }
        }

        /// <summary>
        /// Quits Office only when this session started it and nothing else is open in it (the user may have opened a
        /// file in the hidden instance meanwhile; then it is shown instead). Our own file is closed first: Office can
        /// fail to open it and still leave it open (Word shows a PDF it cannot convert as plain text).
        /// </summary>
        private void Quit(dynamic office, bool startedByUs)
        {
            try
            {
                CloseInput(office);
                int open = app switch
                {
                    OfficeApp.Word => office.Documents.Count,
                    OfficeApp.Excel => office.Workbooks.Count,
                    _ => office.Presentations.Count,
                };

                if (!startedByUs)
                {
                    return;
                }

                if (open == 0)
                {
                    if (app == OfficeApp.Word)
                    {
                        office.Quit(SaveChanges: 0);
                    }
                    else
                    {
                        office.Quit();
                    }
                }
                else if (app != OfficeApp.PowerPoint)
                {
                    office.Visible = true;
                }
            }
            catch (COMException)
            {
                // The process is gone already (e.g. killed after a timeout).
            }
        }

        /// <summary>Closes, without saving, every open document whose path is our input.</summary>
        private void CloseInput(dynamic office)
        {
            dynamic documents = app switch
            {
                OfficeApp.Word => office.Documents,
                OfficeApp.Excel => office.Workbooks,
                _ => office.Presentations,
            };

            // COM collections are 1-based; going backwards keeps the indexes valid while closing.
            for (int i = documents.Count; i >= 1; i--)
            {
                dynamic document = documents[i];
                if (!string.Equals((string)document.FullName, input, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                switch (app)
                {
                    case OfficeApp.Word:
                        document.Close(SaveChanges: 0);
                        break;
                    case OfficeApp.Excel:
                        document.Close(SaveChanges: false);
                        break;
                    default:
                        document.Close();
                        break;
                }
            }
        }

        private int[] ProcessIds()
        {
            var processes = Process.GetProcessesByName(ProcessName);
            var ids = processes.Select(p => p.Id).ToArray();
            foreach (var process in processes)
            {
                process.Dispose();
            }

            return ids;
        }

        private static void KillProcesses(IEnumerable<int> ids)
        {
            foreach (var id in ids)
            {
                try
                {
                    using var process = Process.GetProcessById(id);
                    process.Kill();
                    process.WaitForExit(5000);
                }
                catch (Exception e) when (e is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
                {
                    // Already exited.
                }
            }
        }
    }
}
