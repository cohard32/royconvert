using RoyConvert.App.Platform;
using RoyConvert.Core;

namespace RoyConvert.App.Tests;

#pragma warning disable CA1416 // BuildMenuFile only formats text
public class WindowsShellMenuTests
{
    private static readonly string[] Lines =
        WindowsShellMenu.BuildMenuFile(PresetCatalog.Default, p => p.Name).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

    [Fact]
    public void MenuFile_HasOneLinePerPreset_WithIdLabelAndExtensions()
    {
        Assert.Equal(PresetCatalog.Default.Count(p => !p.ReadsDvd), Lines.Length);
        foreach (var line in Lines)
        {
            var fields = line.Split('\t');
            Assert.Equal(3, fields.Length);
            var preset = Assert.Single(PresetCatalog.Default, p => p.Id == fields[0]);
            Assert.Equal(preset.Name, fields[1]);
            Assert.Equal(preset.InputExtensions.Order(StringComparer.OrdinalIgnoreCase), fields[2].Split(' '));
        }
    }

    [Fact]
    public void MenuFile_ListsPlainPresetsFirst_ThenEachSubmenuTogether()
    {
        var groups = Lines.Select(line => line.Split('\t')[1]).Select(name => name.Contains('/') ? name[..name.IndexOf('/')] : null).ToList();

        var firstGrouped = groups.FindIndex(g => g is not null);
        Assert.True(firstGrouped > 0);
        Assert.All(groups.Skip(firstGrouped), g => Assert.NotNull(g));

        var order = groups.Skip(firstGrouped).Distinct().ToList();
        var contiguous = groups.Skip(firstGrouped).Where((g, i) => i == 0 || g != groups[firstGrouped + i - 1]).ToList();
        Assert.Equal(order, contiguous);
    }
}
