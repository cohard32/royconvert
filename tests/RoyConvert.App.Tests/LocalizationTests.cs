using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using RoyConvert.App.Resources;
using RoyConvert.Core;

namespace RoyConvert.App.Tests;

[Collection(UiCultureCollection.Name)]
public class LocalizationTests
{
    private static Preset Preset(string id) => Assert.Single(PresetCatalog.Default, p => p.Id == id);

    [Theory]
    [InlineData("en", "scale75/png", "Scale 75%/PNG")]
    [InlineData("tr", "scale75/png", "%75 küçült/PNG")]
    [InlineData("tr", "rotate-left/jpg", "Sola döndür/JPG")]
    [InlineData("tr", "mp4-small", "MP4 (küçük)")]
    [InlineData("tr", "720p/mp4", "720p/MP4")]
    [InlineData("tr", "png", "PNG")]
    public void PresetNames_TranslateEachSegmentById(string language, string id, string expected)
    {
        var previous = Strings.Culture;
        try
        {
            Strings.Culture = CultureInfo.GetCultureInfo(language);
            Assert.Equal(expected, PresetNames.DisplayName(Preset(id)));
        }
        finally
        {
            Strings.Culture = previous;
        }
    }

    [Theory]
    [InlineData("en", "rotate-left/png", "{dir}/{name} (rotated)")]
    [InlineData("tr", "rotate-right/mp4", "{dir}/{name} (döndürülmüş)")]
    [InlineData("tr", "mp4-small", "{dir}/{name}-küçük")]
    [InlineData("tr", "combine-pdf", "{dir}/{name} (birleştirilmiş)")]
    [InlineData("tr", "scale50/png", "{dir}/{name}-50%")]
    [InlineData("tr", "png", "{dir}/{name}")]
    public void OutputFileNames_UseTheUiLanguage(string language, string id, string expected)
    {
        var previous = Strings.Culture;
        try
        {
            Strings.Culture = CultureInfo.GetCultureInfo(language);
            Assert.Equal(expected, PresetNames.WithLocalizedFileName(Preset(id)).FileNameTemplate);
        }
        finally
        {
            Strings.Culture = previous;
        }
    }

    [Fact]
    public void Translations_HaveTheSameKeysAndPlaceholders()
    {
        var english = ReadResx("Strings.resx");
        var turkish = ReadResx("Strings.tr.resx");

        Assert.Equal(english.Keys.Order(), turkish.Keys.Order());
        foreach (var (key, text) in english)
        {
            Assert.True(Placeholders(text).SetEquals(Placeholders(turkish[key])), $"Placeholders differ for {key}");
        }
    }

    [Fact]
    public void Settings_RoundTrip_AndUnreadableFileGivesDefaults()
    {
        var path = Path.Combine(Path.GetTempPath(), $"rc-settings-{Guid.NewGuid():N}", "settings.json");
        try
        {
            new AppSettings { Language = "tr" }.Save(path);
            Assert.Equal("tr", AppSettings.Load(path).Language);

            File.WriteAllText(path, "{ not json");
            Assert.Null(AppSettings.Load(path).Language);
            Assert.Null(AppSettings.Load(path + ".missing").Language);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }

    private static HashSet<string> Placeholders(string text) => Regex.Matches(text, @"\{\d+\}").Select(m => m.Value).ToHashSet();

    private static Dictionary<string, string> ReadResx(string name)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(directory.FullName, "RoyConvert.sln")))
        {
            directory = directory.Parent ?? throw new DirectoryNotFoundException("RoyConvert.sln not found.");
        }

        return XDocument.Load(Path.Combine(directory.FullName, "src", "RoyConvert.App", "Resources", name))
            .Root!.Elements("data")
            .ToDictionary(d => d.Attribute("name")!.Value, d => d.Element("value")!.Value);
    }
}
