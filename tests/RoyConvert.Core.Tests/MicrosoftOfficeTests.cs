using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using RoyConvert.Core.Office;

namespace RoyConvert.Core.Tests;

/// <summary>
/// Office → PDF with the installed Microsoft Word, Excel and PowerPoint. Tests that need them do nothing when they are
/// not installed (or on macOS). The sample documents are made by Office itself.
/// </summary>
public sealed class MicrosoftOfficeTests : IDisposable
{
    private static readonly MicrosoftOfficeConverter? Office =
        OperatingSystem.IsWindows() ? MicrosoftOfficeConverter.FindInstalled() : null;

    private readonly string _dir = Directory.CreateTempSubdirectory("royconvert-tests-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Theory]
    [InlineData(OfficeApp.Word, "rapor ğüş.docx")]
    [InlineData(OfficeApp.Excel, "tablo ğüş.xlsx")]
    [InlineData(OfficeApp.PowerPoint, "sunum ğüş.pptx")]
    public async Task ExportsPdf_AndLeavesTheSourceUnchanged(OfficeApp app, string name)
    {
        if (!OperatingSystem.IsWindows() || Office is null || !Office.Installed.Contains(app))
        {
            return;
        }

        var input = Path.Combine(_dir, name);
        var running = Running(app);
        await CreateDocument(app, input);
        await AssertOfficeExited(app, running);
        var hash = SHA256.HashData(File.ReadAllBytes(input));
        var written = File.GetLastWriteTimeUtc(input);
        var job = new ConversionJob(input, PresetCatalog.Find(PresetCatalog.Default, "pdf")!);

        using var runner = ConversionRunner.CreateDefault(null, 1, microsoftOffice: Office);
        await runner.RunAsync([job]);

        Assert.True(job.State == JobState.Done, job.Error);
        Assert.Equal(Path.ChangeExtension(input, ".pdf"), job.Output);
        using (var pdf = PdfiumDocument.Open(job.Output!))
        {
            Assert.True(pdf.PageCount >= 1);
        }

        Assert.Equal(hash, SHA256.HashData(File.ReadAllBytes(input)));
        Assert.Equal(written, File.GetLastWriteTimeUtc(input));
        Assert.Empty(Directory.GetFiles(_dir, "~$*"));
        await AssertOfficeExited(app, running);
    }

    [Theory]
    [InlineData(OfficeApp.Word, "secret.docx")]
    [InlineData(OfficeApp.Excel, "secret.xlsx")]
    [InlineData(OfficeApp.PowerPoint, "secret.pptx")]
    public async Task PasswordProtectedDocument_FailsWithoutWaiting(OfficeApp app, string name)
    {
        if (!OperatingSystem.IsWindows() || Office is null || !Office.Installed.Contains(app))
        {
            return;
        }

        var input = Path.Combine(_dir, name);
        var running = Running(app);
        await CreateDocument(app, input, password: "1234");
        await AssertOfficeExited(app, running);
        var job = new ConversionJob(input, PresetCatalog.Find(PresetCatalog.Default, "pdf")!);
        var watch = Stopwatch.StartNew();

        using var runner = ConversionRunner.CreateDefault(null, 1, microsoftOffice: Office);
        await runner.RunAsync([job]);

        Assert.Equal(JobState.Failed, job.State);
        Assert.Contains("password", job.Error);
        Assert.True(watch.Elapsed < TimeSpan.FromMinutes(1), $"took {watch.Elapsed}");
        Assert.Empty(Directory.GetFiles(_dir, "*.pdf"));
        await AssertOfficeExited(app, running);
    }

    [Theory]
    [InlineData("a.docx", 0)]
    [InlineData("a.XLSX", 0)]
    [InlineData("a.ppt", 0)]
    [InlineData("a.odt", 2)]
    [InlineData("a.png", null)]
    public void Priority_PrefersMicrosoftFormats(string input, int? expected)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var converter = new MicrosoftOfficeConverter(Enum.GetValues<OfficeApp>().ToHashSet());
        Assert.Equal(expected, converter.Priority(input));
    }

    [Fact]
    public void Priority_NeedsTheMatchingApp()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var wordOnly = new MicrosoftOfficeConverter(new HashSet<OfficeApp> { OfficeApp.Word });
        Assert.Equal(0, wordOnly.Priority("a.docx"));
        Assert.Null(wordOnly.Priority("a.xlsx"));
    }

    [Fact]
    public async Task PdfConverter_TriesTheNextSuiteWhenOneFails()
    {
        var calls = new List<string>();
        var failing = new FakeSuite("Microsoft Office", 0, calls, fail: true);
        var working = new FakeSuite("LibreOffice", 1, calls, fail: false);
        var converter = new PdfConverter(new FakeSuite("image", 0, calls, false), [working, failing]);
        var output = Path.Combine(_dir, "a.pdf");

        await converter.ConvertAsync(Path.Combine(_dir, "a.docx"), output,
            PresetCatalog.Find(PresetCatalog.Default, "pdf")!, new Progress<double?>(), CancellationToken.None);

        Assert.Equal(["Microsoft Office", "LibreOffice"], calls);
        Assert.True(File.Exists(output));
    }

    [Fact]
    public async Task PdfConverter_ReportsEverySuitesError()
    {
        var calls = new List<string>();
        var converter = new PdfConverter(new FakeSuite("image", 0, calls, false),
            [new FakeSuite("Microsoft Office", 0, calls, fail: true), new FakeSuite("LibreOffice", 1, calls, fail: true)]);

        var e = await Assert.ThrowsAsync<ConversionException>(() => converter.ConvertAsync(Path.Combine(_dir, "a.docx"),
            Path.Combine(_dir, "a.pdf"), PresetCatalog.Find(PresetCatalog.Default, "pdf")!, new Progress<double?>(),
            CancellationToken.None));

        Assert.Contains("Microsoft Office: broken", e.Message);
        Assert.Contains("LibreOffice: broken", e.Message);
    }

    private sealed class FakeSuite(string name, int priority, List<string> calls, bool fail) : IDocumentConverter
    {
        public string Name => name;

        public int? Priority(string input) => priority;

        public Task ConvertAsync(string input, string output, Preset preset, IProgress<double?> progress, CancellationToken cancellationToken)
        {
            calls.Add(name);
            if (fail)
            {
                throw new ConversionException("broken");
            }

            File.WriteAllText(output, "%PDF");
            return Task.CompletedTask;
        }
    }

    private static string ProcessName(OfficeApp app) => app switch
    {
        OfficeApp.Word => "WINWORD",
        OfficeApp.Excel => "EXCEL",
        _ => "POWERPNT",
    };

    private static HashSet<int> Running(OfficeApp app) =>
        Process.GetProcessesByName(ProcessName(app)).Select(p => p.Id).ToHashSet();

    /// <summary>
    /// The converter quits the Office process it started; it may take a moment to exit. While the other test classes
    /// run in parallel Excel has needed more than 20 s.
    /// </summary>
    private static async Task AssertOfficeExited(OfficeApp app, HashSet<int> before)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(60);
        while (Running(app).Except(before).Any() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(250);
        }

        Assert.Empty(Running(app).Except(before));
    }

    /// <summary>Has Office write a small document with Turkish text, then quits it.</summary>
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static Task CreateDocument(OfficeApp app, string path, string? password = null)
    {
        var done = new TaskCompletionSource();
        var thread = new Thread(() =>
        {
            try
            {
                dynamic office = Activator.CreateInstance(Type.GetTypeFromProgID(app + ".Application")!)!;
                try
                {
                    office.DisplayAlerts = app == OfficeApp.PowerPoint ? 1 : 0;
                    switch (app)
                    {
                        case OfficeApp.Word:
                            dynamic document = office.Documents.Add();
                            document.Content.Text = "Merhaba royconvert: ğüşıöç ĞÜŞİÖÇ";
                            document.SaveAs2(FileName: path, FileFormat: 16 /* wdFormatXMLDocument */, Password: password ?? "");
                            document.Close(SaveChanges: 0);
                            break;
                        case OfficeApp.Excel:
                            dynamic workbook = office.Workbooks.Add();
                            workbook.Worksheets[1].Range["A1"].Value = "Şehir";
                            workbook.Worksheets[1].Range["B1"].Value = 42;
                            workbook.SaveAs(Filename: path, FileFormat: 51 /* xlOpenXMLWorkbook */, Password: password ?? "");
                            workbook.Close(SaveChanges: false);
                            break;
                        default:
                            dynamic presentation = office.Presentations.Add(WithWindow: 0);
                            dynamic slide = presentation.Slides.Add(1, 1 /* ppLayoutTitle */);
                            slide.Shapes[1].TextFrame.TextRange.Text = "Sunum ğüş";
                            if (password is not null)
                            {
                                presentation.Password = password;
                            }

                            presentation.SaveAs(FileName: path, FileFormat: 24 /* ppSaveAsOpenXMLPresentation */);
                            presentation.Close();
                            break;
                    }
                }
                finally
                {
                    // PowerPoint may be the user's own instance.
                    if (app != OfficeApp.PowerPoint || office.Presentations.Count == 0)
                    {
                        office.Quit();
                    }

                    Marshal.FinalReleaseComObject(office);
                }

                done.SetResult();
            }
            catch (Exception e)
            {
                done.SetException(e);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return done.Task;
    }
}
