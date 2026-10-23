using System.Diagnostics;

namespace RoyConvert.App.Platform;

public static class FileManager
{
    /// <summary>Opens Explorer/Finder with the file selected.</summary>
    public static void Reveal(string path)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                Process.Start("explorer.exe", $"/select,\"{path}\"");
            }
            else if (OperatingSystem.IsMacOS())
            {
                Process.Start("open", ["-R", path]);
            }
            else
            {
                Process.Start("xdg-open", [Path.GetDirectoryName(path) ?? path]);
            }
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // Nothing useful to do if the file manager cannot be started.
        }
    }
}
