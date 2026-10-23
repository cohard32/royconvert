using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32;
using RoyConvert.Core;

namespace RoyConvert.App.Platform;

/// <summary>
/// Adds a "royconvert" cascading menu to Explorer's context menu for every supported extension, for the
/// current user only (HKCU, no admin rights). On Windows 11 it appears under "Show more options".
/// </summary>
/// <remarks>
/// Layout:
/// <code>
/// HKCU\Software\Classes\SystemFileAssociations\.mp4\shell\royconvert   MUIVerb, Icon, ExtendedSubCommandsKey=royconvert.menu.N
/// HKCU\Software\Classes\royconvert.menu.N\shell\000\command             "royconvert.exe" --preset mkv "%1"
/// HKCU\Software\Classes\royconvert.menu.N\shell\010                     SubCommands="" (submenu, e.g. "720p")
/// </code>
/// Extensions offered the same presets share one menu key.
/// </remarks>
[SupportedOSPlatform("windows")]
public static class WindowsShellMenu
{
    private const string Verb = "royconvert";
    private const string MenuKeyPrefix = "royconvert.menu.";
    private const string ClassesPath = @"Software\Classes";
    private const string AssociationsPath = @"Software\Classes\SystemFileAssociations";

    public static bool IsRegistered()
    {
        using var key = Registry.CurrentUser.OpenSubKey($@"{AssociationsPath}\.mp4\shell\{Verb}");
        return key is not null;
    }

    /// <returns>The number of extensions that received the menu.</returns>
    public static int Register(string exePath, IReadOnlyList<Preset> presets)
    {
        Unregister(notify: false);

        var extensions = presets.SelectMany(p => p.InputExtensions).Distinct(StringComparer.OrdinalIgnoreCase).Order();
        var menus = extensions
            .Select(ext => (Extension: ext, Presets: presets.Where(p => p.InputExtensions.Contains(ext)).ToList()))
            .Where(x => x.Presets.Count > 0)
            .GroupBy(x => string.Join("|", x.Presets.Select(p => p.Id)))
            .ToList();

        using var classes = Registry.CurrentUser.CreateSubKey(ClassesPath);
        var count = 0;
        for (var i = 0; i < menus.Count; i++)
        {
            var menuKeyName = MenuKeyPrefix + i;
            using (var menu = classes.CreateSubKey(menuKeyName))
            {
                WriteEntries(menu, exePath, menus[i].First().Presets);
            }

            foreach (var (extension, _) in menus[i])
            {
                using var verb = Registry.CurrentUser.CreateSubKey($@"{AssociationsPath}\.{extension}\shell\{Verb}");
                verb.SetValue("MUIVerb", "royconvert");
                verb.SetValue("Icon", $"\"{exePath}\",0");
                verb.SetValue("ExtendedSubCommandsKey", menuKeyName);
                count++;
            }
        }

        NotifyAssociationsChanged();
        return count;
    }

    public static void Unregister() => Unregister(notify: true);

    private static void Unregister(bool notify)
    {
        using (var associations = Registry.CurrentUser.OpenSubKey(AssociationsPath))
        {
            foreach (var name in associations?.GetSubKeyNames() ?? [])
            {
                Registry.CurrentUser.DeleteSubKeyTree($@"{AssociationsPath}\{name}\shell\{Verb}", throwOnMissingSubKey: false);
            }
        }

        using (var classes = Registry.CurrentUser.OpenSubKey(ClassesPath, writable: true))
        {
            foreach (var name in classes?.GetSubKeyNames().Where(n => n.StartsWith(MenuKeyPrefix, StringComparison.OrdinalIgnoreCase)) ?? [])
            {
                classes!.DeleteSubKeyTree(name, throwOnMissingSubKey: false);
            }
        }

        if (notify)
        {
            NotifyAssociationsChanged();
        }
    }

    /// <summary>Writes plain presets first, then one submenu per group ("720p/MP4" goes under "720p").</summary>
    private static void WriteEntries(RegistryKey menu, string exePath, IReadOnlyList<Preset> presets)
    {
        using var shell = menu.CreateSubKey("shell");
        var order = 0;

        foreach (var preset in presets.Where(p => !p.Name.Contains('/')))
        {
            WriteCommand(shell, order++, preset.Name, exePath, preset);
        }

        foreach (var group in presets.Where(p => p.Name.Contains('/')).GroupBy(p => p.Name[..p.Name.IndexOf('/')]))
        {
            using var submenu = shell.CreateSubKey(KeyName(order++));
            submenu.SetValue("MUIVerb", group.Key);
            submenu.SetValue("SubCommands", string.Empty);
            using var subShell = submenu.CreateSubKey("shell");
            var subOrder = 0;
            foreach (var preset in group)
            {
                WriteCommand(subShell, subOrder++, preset.Name[(preset.Name.IndexOf('/') + 1)..], exePath, preset);
            }
        }
    }

    private static void WriteCommand(RegistryKey shell, int order, string label, string exePath, Preset preset)
    {
        using var item = shell.CreateSubKey(KeyName(order));
        item.SetValue("MUIVerb", label);
        // "Player" lifts Explorer's 15-item limit; each selected file still starts its own process.
        item.SetValue("MultiSelectModel", "Player");
        using var command = item.CreateSubKey("command");
        command.SetValue(null, $"\"{exePath}\" --preset {preset.Id} \"%1\"");
    }

    // Explorer sorts verbs by key name, so zero-padded numbers keep the catalog order.
    private static string KeyName(int order) => order.ToString("D3");

    private static void NotifyAssociationsChanged() => SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);

    private const int SHCNE_ASSOCCHANGED = 0x08000000;
    private const int SHCNF_IDLIST = 0;

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(int eventId, int flags, IntPtr item1, IntPtr item2);
}
