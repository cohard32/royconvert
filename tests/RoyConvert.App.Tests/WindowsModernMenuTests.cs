using RoyConvert.App.Platform;

namespace RoyConvert.App.Tests;

#pragma warning disable CA1416 // only builds script text
public class WindowsModernMenuTests
{
    [Fact]
    public void AddPackageScript_QuotesPathsForPowerShell()
    {
        var script = WindowsModernMenu.AddPackageScript(@"C:\Ali's Apps\royconvert_shell.msix", @"C:\Ali's Apps");

        Assert.Equal(
            "$ErrorActionPreference = 'Stop'; Add-AppxPackage -Path 'C:\\Ali''s Apps\\royconvert_shell.msix' "
            + "-ExternalLocation 'C:\\Ali''s Apps' -AllowUnsigned -ForceUpdateFromAnyVersion",
            script);
    }

    [Fact]
    public void PackageName_MatchesManifest()
    {
        var manifest = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "RoyConvert.Shell", "package", "AppxManifest.xml"));

        Assert.Contains($"<Identity Name=\"{WindowsModernMenu.PackageName}\"", manifest);
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(directory.FullName, "RoyConvert.sln")))
        {
            directory = directory.Parent ?? throw new DirectoryNotFoundException("RoyConvert.sln not found.");
        }

        return directory.FullName;
    }
}
