using RoyConvert.App.Resources;
using RoyConvert.Core;

namespace RoyConvert.App;

/// <summary>
/// A menu item that opens a window of its own instead of converting. The Explorer menu starts every item as
/// "--preset &lt;id&gt;", so these ids must not be preset ids.
/// </summary>
/// <param name="Label">The menu label in the current UI language.</param>
public sealed record MenuTool(string Id, CommandKind Kind, Func<string> Label, IReadOnlySet<string> Extensions);

public static class MenuTools
{
    public static MenuTool Watermark { get; } =
        new("watermark", CommandKind.Watermark, () => Strings.Watermark_Menu, Core.Watermark.Inputs);

    public static MenuTool Trim { get; } = new("trim", CommandKind.Trim, () => Strings.Trim_Menu, MediaTrim.Inputs);

    /// <summary>Any mix of the files royconvert knows.</summary>
    public static MenuTool Rename { get; } = new("rename", CommandKind.Rename, () => Strings.Rename_Menu,
        PresetCatalog.Default.SelectMany(p => p.InputExtensions).ToHashSet(StringComparer.OrdinalIgnoreCase));

    /// <summary>In menu order, after the presets.</summary>
    public static IReadOnlyList<MenuTool> All { get; } = [Trim, Watermark, Rename];

    public static MenuTool? Find(string id) =>
        All.FirstOrDefault(tool => string.Equals(tool.Id, id, StringComparison.OrdinalIgnoreCase));
}
