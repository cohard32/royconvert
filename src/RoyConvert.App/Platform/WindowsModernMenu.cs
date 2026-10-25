using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text;
using RoyConvert.App.Resources;

namespace RoyConvert.App.Platform;

/// <summary>
/// Windows 11's compact context menu only shows handlers that belong to a package. royconvert_shell.msix is a sparse
/// package (manifest and logos only) whose external location is the install folder, so Explorer loads
/// royconvert_shell.dll's IExplorerCommand from there. The package is unsigned, which Add-AppxPackage accepts with
/// -AllowUnsigned on Windows 11; Windows may require administrator rights for it, so a failed install is retried elevated.
/// </summary>
[SupportedOSPlatform("windows")]
public static class WindowsModernMenu
{
    public const string PackageFileName = "royconvert_shell.msix";
    public const string PackageName = "royconvert.ContextMenu";

    public static bool IsSupported => OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000);

    /// <returns>Null on success, otherwise why the menu could not be installed.</returns>
    public static string? Register(string directory)
    {
        var package = Path.Combine(directory, PackageFileName);
        if (!File.Exists(package))
        {
            return string.Format(Strings.Shell_PackageMissing, package);
        }

        var script = AddPackageScript(package, directory);
        return RunPowerShell(script, elevated: false) == 0
            ? null
            : RunPowerShellElevated(script);
    }

    /// <returns>Null on success, otherwise why the package could not be removed.</returns>
    public static string? Unregister()
        => RunPowerShell(RemovePackageScript(), elevated: false) == 0 ? null : Strings.Shell_PackageRemoveFailed;

    public static string AddPackageScript(string package, string directory)
        => $"$ErrorActionPreference = 'Stop'; Add-AppxPackage -Path {Quote(package)} -ExternalLocation {Quote(directory)} -AllowUnsigned -ForceUpdateFromAnyVersion";

    public static string RemovePackageScript()
        => $"$ErrorActionPreference = 'Stop'; Get-AppxPackage -Name {Quote(PackageName)} | Remove-AppxPackage";

    private static string Quote(string text) => "'" + text.Replace("'", "''") + "'";

    private static string? RunPowerShellElevated(string script)
    {
        try
        {
            return RunPowerShell(script, elevated: true) == 0
                ? null
                : Strings.Shell_PackageRejected;
        }
        catch (Win32Exception e) when (e.NativeErrorCode == 1223) // ERROR_CANCELLED: the UAC prompt was declined
        {
            return Strings.Shell_PackageNeedsAdmin;
        }
    }

    private static int RunPowerShell(string script, bool elevated)
    {
        // -EncodedCommand avoids quoting the script for the command line.
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        var info = new ProcessStartInfo("powershell.exe", $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {encoded}")
        {
            UseShellExecute = elevated,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
        };
        if (elevated)
        {
            info.Verb = "runas";
        }

        using var process = Process.Start(info)!;
        process.WaitForExit();
        return process.ExitCode;
    }
}
