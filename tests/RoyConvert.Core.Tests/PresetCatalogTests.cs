namespace RoyConvert.Core.Tests;

public class PresetCatalogTests
{
    private static readonly IReadOnlyList<Preset> Presets = PresetCatalog.Default;

    [Fact]
    public void IdsAreUnique()
    {
        var duplicates = Presets.GroupBy(p => p.Id, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => g.Key);
        Assert.Empty(duplicates);
    }

    [Fact]
    public void NamesAreUnique()
    {
        Assert.Equal(Presets.Count, Presets.Select(p => p.Name).Distinct().Count());
    }

    [Fact]
    public void EveryOutputFormatHasAPlainPreset()
    {
        foreach (var format in Enum.GetValues<OutputFormat>())
        {
            Assert.Contains(Presets, p => p.Output == format && !p.Name.Contains('/'));
        }
    }

    [Fact]
    public void Find_IsCaseInsensitive()
    {
        Assert.Equal(OutputFormat.Mp4, PresetCatalog.Find(Presets, "720P/MP4")?.Output);
        Assert.Null(PresetCatalog.Find(Presets, "nope"));
    }

    [Fact]
    public void Video_OffersVideoAndAudioButNotImages()
    {
        var offered = PresetCatalog.ForFiles(Presets, ["a.mov"]).ToList();

        Assert.Contains(offered, p => p.Id == "mp4");
        Assert.Contains(offered, p => p.Id == "mp3");
        Assert.Contains(offered, p => p.Id == "gif");
        Assert.DoesNotContain(offered, p => Formats.KindOf(p.Output) == MediaKind.Image);
    }

    [Fact]
    public void PlainPreset_IsNotOfferedForItsOwnFormat()
    {
        var offered = PresetCatalog.ForFiles(Presets, ["a.MP4"]).Select(p => p.Id).ToList();

        Assert.DoesNotContain("mp4", offered);
        Assert.Contains("720p/mp4", offered);
        Assert.DoesNotContain("jpg", PresetCatalog.ForFiles(Presets, ["photo.jpeg"]).Select(p => p.Id));
    }

    [Fact]
    public void Audio_OffersOnlyAudio()
    {
        var offered = PresetCatalog.ForFiles(Presets, ["song.flac"]).ToList();

        Assert.NotEmpty(offered);
        Assert.All(offered, p => Assert.Equal(MediaKind.Audio, Formats.KindOf(p.Output)));
    }

    [Fact]
    public void MixedSelection_OffersOnlyCommonPresets()
    {
        Assert.Empty(PresetCatalog.ForFiles(Presets, ["a.png", "b.mp3"]));
        Assert.Contains(PresetCatalog.ForFiles(Presets, ["a.mov", "b.wav"]), p => p.Id == "mp3");
        // Converting an mp3 to mp3 is pointless, so the plain preset disappears once one is selected.
        Assert.DoesNotContain(PresetCatalog.ForFiles(Presets, ["a.mov", "b.mp3"]), p => p.Id == "mp3");
    }

    [Fact]
    public void Pdf_OffersPageImages()
    {
        var offered = PresetCatalog.ForFiles(Presets, ["report.PDF"]).Select(p => p.Id).Order();

        Assert.Equal(["combine-pdf", "docx", "jpg", "png", "split-pdf", "webp"], offered);
    }

    [Fact]
    public void OfficeDocuments_OfferPdf()
    {
        var offered = PresetCatalog.ForFiles(Presets, ["a.docx", "b.xlsx", "c.pptx", "d.odt"]).Select(p => p.Id);

        Assert.Equal(["pdf"], offered);
    }

    [Fact]
    public void ImagesAndDocuments_ShareThePdfPreset()
    {
        Assert.Contains(PresetCatalog.ForFiles(Presets, ["a.jpg", "b.heic"]), p => p.Id == "pdf");
        Assert.Contains(PresetCatalog.ForFiles(Presets, ["a.jpg", "b.docx"]), p => p.Id == "pdf");
        Assert.DoesNotContain(PresetCatalog.ForFiles(Presets, ["a.pdf"]), p => p.Id == "pdf");
    }

    [Fact]
    public void EmptySelection_OffersNothing()
    {
        Assert.Empty(PresetCatalog.ForFiles(Presets, []));
    }
}
