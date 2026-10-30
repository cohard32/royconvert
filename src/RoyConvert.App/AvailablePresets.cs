using RoyConvert.Core;
using RoyConvert.Core.Office;

namespace RoyConvert.App;

/// <summary>The built-in presets this computer can run, for the main window and the right-click menu.</summary>
public static class AvailablePresets
{
    /// <summary>PDF → Word (DOCX) is left out without Microsoft Word: nothing else can convert PDF to Word.</summary>
    public static IReadOnlyList<Preset> For(bool hasWord) =>
        hasWord ? PresetCatalog.Default : PresetCatalog.Default.Where(p => p.Output != OutputFormat.Docx).ToList();

    public static bool HasWord() =>
        OperatingSystem.IsWindows() && MicrosoftOfficeConverter.FindInstalled()?.Installed.Contains(OfficeApp.Word) == true;
}
