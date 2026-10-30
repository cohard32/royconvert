using System.Diagnostics;

namespace RoyConvert.App.Platform;

/// <summary>Runs a downloaded setup over the installed royconvert, the same way it was installed.</summary>
public static class UpdateInstaller
{
    /// <summary>
    /// Setup options: no questions, a progress window only, no restart. An install in Program Files was made for all
    /// users and needs administrator rights again (Windows asks); one elsewhere was made for this user only. Setup
    /// closes this royconvert itself (CloseApplications) and keeps the earlier choices, such as the right-click menu.
    /// </summary>
    public static IReadOnlyList<string> Arguments(string installFolder)
    {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var forAllUsers = Path.GetFullPath(installFolder).StartsWith(Path.GetFullPath(programFiles) + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase);
        return ["/SILENT", "/SUPPRESSMSGBOXES", "/NORESTART", forAllUsers ? "/ALLUSERS" : "/CURRENTUSER"];
    }

    /// <exception cref="System.ComponentModel.Win32Exception">Setup could not be started (e.g. the user said no to administrator rights).</exception>
    public static void Start(string setupPath)
    {
        var start = new ProcessStartInfo(setupPath) { UseShellExecute = true };
        foreach (var argument in Arguments(AppContext.BaseDirectory))
        {
            start.ArgumentList.Add(argument);
        }

        Process.Start(start);
    }
}
