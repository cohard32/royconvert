using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Microsoft.Win32;
using RoyConvert.App.Resources;
using RoyConvert.Core;

namespace RoyConvert.App.Platform;

/// <summary>
/// Adds a "royconvert" menu to Explorer's context menu for every supported extension, for the current user only
/// (HKCU, no admin rights). The items come from royconvert_shell.dll next to the exe, which reads
/// <see cref="MenuFilePath"/>. Registry-only menus (static verbs, even with ExplorerCommandHandler) are cut off after
/// 16 items on Windows 10; a context menu handler builds its own popup menus and has no such limit.
/// </summary>
/// <remarks>
/// Layout:
/// <code>
/// HKCU\Software\Classes\CLSID\{34C9CD3A-…}\InprocServer32                                  "…\royconvert_shell.dll", Apartment
/// HKCU\Software\Classes\SystemFileAssociations\.mp4\shellex\ContextMenuHandlers\royconvert   {34C9CD3A-…}
/// HKCU\Software\Classes\Directory\shellex\ContextMenuHandlers\royconvert                     {34C9CD3A-…}  (DVDs; also Drive)
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
    /// <summary>Per user, in that user's language; the install folder may not be writable.</summary>
    public static string MenuFilePath { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "royconvert", "shell-menu.txt");

    /// <summary>The "extension" of DVD presets in the menu file; no file name can end in it.</summary>
    public const string DvdInput = "<dvd>";

    private const string Name = "royconvert";
    private const string ClassesPath = @"Software\Classes";
    // Folders and drives get the menu for DVDs (a VIDEO_TS folder, a folder or drive that contains one).
    private static readonly string[] FolderClasses = ["Directory", "Drive"];
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
            throw new FileNotFoundException(string.Format(Strings.Shell_HandlerMissing, handler), handler);
        }

        Unregister(notify: false);
        WriteMenuFile(presets);

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

        if (presets.Any(p => p.ReadsDvd))
        {
            foreach (var folderClass in FolderClasses)
            {
                using var key = Registry.CurrentUser.CreateSubKey(FolderHandlerKeyPath(folderClass));
                key.SetValue(null, ContextMenuClsid);
            }
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

    /// <summary>Rewrites the menu labels in the current UI language, if the menu is installed.</summary>
    public static void RefreshMenuFile(IReadOnlyList<Preset> presets)
    {
        if (IsRegistered())
        {
            WriteMenuFile(presets);
        }
    }

    /// <summary>
    /// One line per preset: id, menu label (from <paramref name="label"/>) and input extensions, tab separated.
    /// Presets without a submenu come first, then the submenus in catalog order; the handler keeps this order.
    /// DVD presets get <see cref="DvdInput"/> instead of extensions: the handler offers them for DVD folders and drives.
    /// </summary>
    public static string BuildMenuFile(IReadOnlyList<Preset> presets, Func<Preset, string> label)
    {
        var ordered = presets.Where(p => !p.Name.Contains('/'))
            .Concat(presets.Where(p => p.Name.Contains('/')).GroupBy(p => p.Name[..p.Name.IndexOf('/')]).SelectMany(g => g));
        var text = new StringBuilder();
        foreach (var preset in ordered)
        {
            text.Append(preset.Id).Append('\t').Append(label(preset)).Append('\t');
            if (preset.ReadsDvd)
            {
                text.Append(DvdInput);
            }
            else
            {
                text.AppendJoin(' ', preset.InputExtensions.Order(StringComparer.OrdinalIgnoreCase));
            }

            text.Append("\r\n");
        }

        return text.ToString();
    }

    private static void WriteMenuFile(IReadOnlyList<Preset> presets)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(MenuFilePath)!);
        File.WriteAllText(MenuFilePath, BuildMenuFile(presets, PresetNames.DisplayName), new UTF8Encoding(false));
    }

    private static string HandlerKeyPath(string extension) => $@"{AssociationsPath}\.{extension}\shellex\ContextMenuHandlers\{Name}";

    private static string FolderHandlerKeyPath(string folderClass) => $@"{ClassesPath}\{folderClass}\shellex\ContextMenuHandlers\{Name}";

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

        foreach (var folderClass in FolderClasses)
        {
            Registry.CurrentUser.DeleteSubKeyTree(FolderHandlerKeyPath(folderClass), throwOnMissingSubKey: false);
        }

        using (var classes = Registry.CurrentUser.OpenSubKey(ClassesPath, writable: true))
        {
            foreach (var name in classes?.GetSubKeyNames().Where(n => n.StartsWith(LegacyMenuKeyPrefix, StringComparison.OrdinalIgnoreCase)) ?? [])
            {
                classes!.DeleteSubKeyTree(name, throwOnMissingSubKey: false);
            }
        }

        Registry.CurrentUser.DeleteSubKeyTree(ContextMenuServerPath, throwOnMissingSubKey: false);
        if (File.Exists(MenuFilePath))
        {
            File.Delete(MenuFilePath);
        }
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
