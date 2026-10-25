using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Microsoft.Win32;
using RoyConvert.Core;

namespace RoyConvert.App.Platform;

/// <summary>
/// Adds a "royconvert" menu to Explorer's context menu for every supported extension, for the current user only
/// (HKCU, no admin rights). The items come from royconvert_shell.dll next to the exe, which reads
/// <see cref="MenuFileName"/>. Registry-only menus (static verbs, even with ExplorerCommandHandler) are cut off after
/// 16 items on Windows 10; a context menu handler builds its own popup menus and has no such limit.
/// </summary>
/// <remarks>
/// Layout:
/// <code>
/// HKCU\Software\Classes\CLSID\{34C9CD3A-…}\InprocServer32                                  "…\royconvert_shell.dll", Apartment
/// HKCU\Software\Classes\SystemFileAssociations\.mp4\shellex\ContextMenuHandlers\royconvert   {34C9CD3A-…}
/// </code>
/// On Windows 11 this menu appears under "Show more options"; <see cref="WindowsModernMenu"/> adds the compact menu entry.
/// </remarks>
[SupportedOSPlatform("windows")]
public static class WindowsShellMenu
{
    /// <param name="Extensions">The number of extensions that received the menu.</param>
    /// <param name="ModernMenuError">Why the Windows 11 menu could not be installed; null if it was or is not needed.</param>
    public sealed record Registration(int Extensions, string? ModernMenuError);

    /// <summary>IShellExtInit + IContextMenu: the classic menu.</summary>
    public const string ContextMenuClsid = "{34C9CD3A-4231-4BBA-B15E-9C1DF9B05DB2}";

    public const string HandlerFileName = "royconvert_shell.dll";
    public const string MenuFileName = "shell-menu.txt";

    private const string Name = "royconvert";
    private const string ClassesPath = @"Software\Classes";
    private const string AssociationsPath = @"Software\Classes\SystemFileAssociations";
    private const string ContextMenuServerPath = $@"Software\Classes\CLSID\{ContextMenuClsid}";

    // Earlier registry-only menu (static verbs and their cascade keys); removed on (un)registration.
    private const string LegacyMenuKeyPrefix = "royconvert.menu.";
    private const string LegacyCommandServerPath = @"Software\Classes\CLSID\{F350E4E0-298E-47B5-9E43-F82D43D99A06}";

    public static bool IsRegistered()
    {
        using var key = Registry.CurrentUser.OpenSubKey(HandlerKeyPath("mp4"));
        return key is not null;
    }

    /// <exception cref="FileNotFoundException">royconvert_shell.dll is not next to <paramref name="exePath"/>.</exception>
    public static Registration Register(string exePath, IReadOnlyList<Preset> presets)
    {
        var directory = Path.GetDirectoryName(exePath)!;
        var handler = Path.Combine(directory, HandlerFileName);
        if (!File.Exists(handler))
        {
            throw new FileNotFoundException($"The context menu handler is missing: {handler}", handler);
        }

        Unregister(notify: false);
        File.WriteAllText(Path.Combine(directory, MenuFileName), BuildMenuFile(presets), new UTF8Encoding(false));

        using (var server = Registry.CurrentUser.CreateSubKey($@"{ContextMenuServerPath}\InprocServer32"))
        {
            server.SetValue(null, handler);
            server.SetValue("ThreadingModel", "Apartment");
        }

        var extensions = presets.SelectMany(p => p.InputExtensions).Distinct(StringComparer.OrdinalIgnoreCase).Order().ToList();
        foreach (var extension in extensions)
        {
            using var key = Registry.CurrentUser.CreateSubKey(HandlerKeyPath(extension));
            key.SetValue(null, ContextMenuClsid);
        }

        NotifyAssociationsChanged();
        return new Registration(extensions.Count, WindowsModernMenu.IsSupported ? WindowsModernMenu.Register(directory) : null);
    }

    /// <returns>Null on success, otherwise why the Windows 11 menu could not be removed.</returns>
    public static string? Unregister()
    {
        Unregister(notify: true);
        return WindowsModernMenu.IsSupported ? WindowsModernMenu.Unregister() : null;
    }

    /// <summary>
    /// One line per preset: id, menu label and input extensions, tab separated. Presets without a submenu come
    /// first, then the submenus in catalog order; the handler keeps this order.
    /// </summary>
    public static string BuildMenuFile(IReadOnlyList<Preset> presets)
    {
        var ordered = presets.Where(p => !p.Name.Contains('/'))
            .Concat(presets.Where(p => p.Name.Contains('/')).GroupBy(p => p.Name[..p.Name.IndexOf('/')]).SelectMany(g => g));
        var text = new StringBuilder();
        foreach (var preset in ordered)
        {
            text.Append(preset.Id).Append('\t').Append(preset.Name).Append('\t')
                .AppendJoin(' ', preset.InputExtensions.Order(StringComparer.OrdinalIgnoreCase)).Append("\r\n");
        }

        return text.ToString();
    }

    private static string HandlerKeyPath(string extension) => $@"{AssociationsPath}\.{extension}\shellex\ContextMenuHandlers\{Name}";

    private static void Unregister(bool notify)
    {
        using (var associations = Registry.CurrentUser.OpenSubKey(AssociationsPath))
        {
            foreach (var name in associations?.GetSubKeyNames() ?? [])
            {
                Registry.CurrentUser.DeleteSubKeyTree($@"{AssociationsPath}\{name}\shellex\ContextMenuHandlers\{Name}", throwOnMissingSubKey: false);
                Registry.CurrentUser.DeleteSubKeyTree($@"{AssociationsPath}\{name}\shell\{Name}", throwOnMissingSubKey: false);
            }
        }

        using (var classes = Registry.CurrentUser.OpenSubKey(ClassesPath, writable: true))
        {
            foreach (var name in classes?.GetSubKeyNames().Where(n => n.StartsWith(LegacyMenuKeyPrefix, StringComparison.OrdinalIgnoreCase)) ?? [])
            {
                classes!.DeleteSubKeyTree(name, throwOnMissingSubKey: false);
            }
        }

        Registry.CurrentUser.DeleteSubKeyTree(ContextMenuServerPath, throwOnMissingSubKey: false);
        Registry.CurrentUser.DeleteSubKeyTree(LegacyCommandServerPath, throwOnMissingSubKey: false);

        if (notify)
        {
            NotifyAssociationsChanged();
        }
    }

    private static void NotifyAssociationsChanged() => SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);

    private const int SHCNE_ASSOCCHANGED = 0x08000000;
    private const int SHCNF_IDLIST = 0;

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(int eventId, int flags, IntPtr item1, IntPtr item2);
}
