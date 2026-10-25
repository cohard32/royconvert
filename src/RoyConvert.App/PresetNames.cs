using Avalonia.Data.Converters;
using RoyConvert.App.Resources;
using RoyConvert.Core;

namespace RoyConvert.App;

/// <summary>Preset names in the UI language.</summary>
public static class PresetNames
{
    public static FuncValueConverter<Preset?, string> Converter { get; } = new(p => p is null ? string.Empty : DisplayName(p));

    /// <summary>
    /// The menu path with each segment translated by the matching id segment ("Preset_scale75"):
    /// "Scale 75%/PNG" (id "scale75/png") becomes "%75 küçült/PNG" in Turkish. Untranslated segments stay as they are.
    /// </summary>
    public static string DisplayName(Preset preset)
    {
        var names = preset.Name.Split('/');
        var ids = preset.Id.Split('/');
        if (names.Length == ids.Length)
        {
            for (var i = 0; i < names.Length; i++)
            {
                if (Strings.ResourceManager.GetString("Preset_" + ids[i], Strings.Culture) is { } translated)
                {
                    names[i] = translated;
                }
            }
        }

        return string.Join('/', names);
    }
}
