using System.IO.Compression;
using System.Security;
using System.Text;

namespace RoyConvert.Core.Tests;

/// <summary>Spreadsheets → CSV with LibreOffice: one file per sheet, ';' between fields, UTF-8 with a byte order mark.</summary>
public sealed class SpreadsheetCsvTests : IDisposable
{
    private static readonly byte[] Bom = [0xEF, 0xBB, 0xBF];

    private readonly string _dir = Directory.CreateTempSubdirectory("royconvert-csv-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static Preset Csv => PresetCatalog.Find(PresetCatalog.Default, "csv")!;

    [Fact]
    public void CsvPreset_TakesSpreadsheetsOnly()
    {
        Assert.True(Csv.Accepts("a.xlsx"));
        Assert.True(Csv.Accepts("a.XLS"));
        Assert.True(Csv.Accepts("a.ods"));
        Assert.False(Csv.Accepts("a.docx"));
        Assert.False(Csv.Accepts("a.csv"));
        Assert.Equal(MediaKind.Document, Formats.KindOf(OutputFormat.Csv));
    }

    [Fact]
    public void OnlyLibreOffice_ExportsCsv()
    {
        var libreOffice = new Office.OfficeConverter("soffice.exe");
        Assert.NotNull(libreOffice.Priority("a.xlsx", OutputFormat.Csv));
        Assert.Null(libreOffice.Priority("a.docx", OutputFormat.Csv));
        Assert.Contains(Office.OfficeConverter.CsvFilter, Office.OfficeConverter.Arguments("a.xlsx", "p", "o", Office.OfficeConverter.CsvFilter));
    }

    [Fact]
    public void MoveSheets_OneSheet_IsTheOutput_SeveralAreNamedAfterTheirSheet()
    {
        var work = Directory.CreateDirectory(Path.Combine(_dir, "work")).FullName;
        File.WriteAllText(Path.Combine(work, "tablo-Sheet1.csv"), "a;b", new UTF8Encoding(false));
        var output = Path.Combine(_dir, "tablo.csv");

        Assert.Equal([output], Office.OfficeConverter.MoveSheets("tablo.xlsx", work, output));
        Assert.Equal([.. Bom, .. "a;b"u8.ToArray()], File.ReadAllBytes(output));

        var several = Directory.CreateDirectory(Path.Combine(_dir, "several")).FullName;
        File.WriteAllText(Path.Combine(several, "tablo-Satışlar.csv"), "x", new UTF8Encoding(true)); // BOM kept, not doubled
        File.WriteAllText(Path.Combine(several, "tablo-Özet 2026.csv"), "y", new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(_dir, "tablo-Satışlar.csv"), "already there");

        var written = Office.OfficeConverter.MoveSheets("tablo.xlsx", several, Path.Combine(_dir, "tablo (1).csv"));

        Assert.Equal([Path.Combine(_dir, "tablo (1)-Satışlar.csv"), Path.Combine(_dir, "tablo (1)-Özet 2026.csv")], written);
        Assert.Equal([.. Bom, .. "x"u8.ToArray()], File.ReadAllBytes(written[0]));
        Assert.Equal("already there", File.ReadAllText(Path.Combine(_dir, "tablo-Satışlar.csv")));
    }

    [Fact]
    public void MoveSheets_NothingWritten_IsAConversionError()
    {
        var work = Directory.CreateDirectory(Path.Combine(_dir, "empty")).FullName;
        Assert.Throws<ConversionException>(() => Office.OfficeConverter.MoveSheets("a.xlsx", work, Path.Combine(_dir, "a.csv")));
    }

    [Fact]
    public async Task WithoutLibreOffice_FailsWithHint()
    {
        var input = Path.Combine(_dir, "tablo.xlsx");
        CreateXlsx(input, ("Sayfa1", [["a"]]));
        var job = new ConversionJob(input, Csv);

        using var runner = ConversionRunner.CreateDefault(null, 1);
        await runner.RunAsync([job]);

        Assert.Equal(JobState.Failed, job.State);
        Assert.Contains("LibreOffice", job.Error);
    }

    [Fact]
    public async Task WithLibreOffice_EverySheetBecomesACsv()
    {
        if (ToolLocator.FindLibreOffice() is not { } soffice)
        {
            return;
        }

        var input = Path.Combine(_dir, "tablo ğ.xlsx");
        CreateXlsx(input,
            ("Satışlar", [["Ürün", "Not"], ["Çay", "iç \"özel\"; dikkat"], ["Şeker", "ğüşıöç ĞÜŞİÖÇ"]]),
            ("Özet", [["Ay", "Kasım"]]));
        var single = Path.Combine(_dir, "tek.xlsx");
        CreateXlsx(single, ("Sayfa1", [["a", "b"]]));
        ConversionJob[] jobs = [new(input, Csv), new(single, Csv)];

        using var runner = ConversionRunner.CreateDefault(null, 2, soffice);
        await runner.RunAsync(jobs);

        Assert.All(jobs, j => Assert.True(j.State == JobState.Done, j.Error));
        var sales = File.ReadAllBytes(Path.Combine(_dir, "tablo ğ-Satışlar.csv"));
        Assert.Equal(Bom, sales[..3]);
        Assert.Equal(
            ["Ürün;Not", "Çay;\"iç \"\"özel\"\"; dikkat\"", "Şeker;ğüşıöç ĞÜŞİÖÇ"],
            Encoding.UTF8.GetString(sales[3..]).Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.TrimEnd('\r')));
        Assert.StartsWith("Ay;Kasım", File.ReadAllText(Path.Combine(_dir, "tablo ğ-Özet.csv")));
        Assert.StartsWith("a;b", File.ReadAllText(Path.Combine(_dir, "tek.csv")));
        Assert.False(File.Exists(Path.Combine(_dir, "tablo ğ.csv")));
    }

    /// <summary>A minimal .xlsx: one worksheet per sheet, every cell an inline string.</summary>
    private static void CreateXlsx(string path, params (string Name, string[][] Rows)[] sheets)
    {
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        void Add(string name, string xml)
        {
            using var writer = new StreamWriter(zip.CreateEntry(name).Open(), new UTF8Encoding(false));
            writer.Write(xml);
        }

        const string ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        const string rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        Add("[Content_Types].xml",
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
            "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
            "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
            "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>" +
            string.Concat(sheets.Select((_, i) =>
                $"<Override PartName=\"/xl/worksheets/sheet{i + 1}.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>")) +
            "</Types>");
        Add("_rels/.rels",
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
            $"<Relationship Id=\"r1\" Type=\"{rel}/officeDocument\" Target=\"xl/workbook.xml\"/></Relationships>");
        Add("xl/workbook.xml",
            $"<?xml version=\"1.0\" encoding=\"UTF-8\"?><workbook xmlns=\"{ns}\" xmlns:r=\"{rel}\"><sheets>" +
            string.Concat(sheets.Select((s, i) => $"<sheet name=\"{SecurityElement.Escape(s.Name)}\" sheetId=\"{i + 1}\" r:id=\"s{i + 1}\"/>")) +
            "</sheets></workbook>");
        Add("xl/_rels/workbook.xml.rels",
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
            string.Concat(sheets.Select((_, i) =>
                $"<Relationship Id=\"s{i + 1}\" Type=\"{rel}/worksheet\" Target=\"worksheets/sheet{i + 1}.xml\"/>")) +
            "</Relationships>");
        for (var i = 0; i < sheets.Length; i++)
        {
            var rows = sheets[i].Rows.Select((cells, r) => $"<row r=\"{r + 1}\">" +
                string.Concat(cells.Select((value, c) =>
                    $"<c r=\"{(char)('A' + c)}{r + 1}\" t=\"inlineStr\"><is><t>{SecurityElement.Escape(value)}</t></is></c>")) +
                "</row>");
            Add($"xl/worksheets/sheet{i + 1}.xml",
                $"<?xml version=\"1.0\" encoding=\"UTF-8\"?><worksheet xmlns=\"{ns}\"><sheetData>{string.Concat(rows)}</sheetData></worksheet>");
        }
    }
}
