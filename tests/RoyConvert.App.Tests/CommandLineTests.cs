using RoyConvert.App.Resources;

namespace RoyConvert.App.Tests;

[Collection(UiCultureCollection.Name)]
public class CommandLineTests
{
    private static readonly string Cwd = Path.Combine(Path.GetTempPath(), "rc-cwd");

    [Fact]
    public void NoArguments_IsHome()
    {
        Assert.Equal(CommandKind.Home, CommandLine.Parse([], Cwd).Kind);
    }

    [Fact]
    public void Convert_ResolvesRelativeAndQuotedPaths()
    {
        var command = CommandLine.Parse(["--preset", "720p/mp4", "a.mov", "\"sub dir\\b.mkv\""], Cwd);

        Assert.Equal(CommandKind.Convert, command.Kind);
        Assert.Equal("720p/mp4", command.PresetId);
        Assert.Equal([Path.Combine(Cwd, "a.mov"), Path.GetFullPath(Path.Combine(Cwd, "sub dir", "b.mkv"))], command.Files);
    }

    [Fact]
    public void InputList_IsRead_IgnoringBlankLines()
    {
        var list = Path.GetTempFileName();
        try
        {
            File.WriteAllLines(list, [@"C:\x\1.mp4", "", @"C:\x\2.mp4"]);
            var command = CommandLine.Parse(["--preset", "mkv", "--input-list", list], Cwd);

            Assert.Equal(CommandKind.Convert, command.Kind);
            Assert.Equal(2, command.Files.Count);
        }
        finally
        {
            File.Delete(list);
        }
    }

    [Theory]
    [InlineData("--input-list", true)]
    [InlineData("--temp-input-list", false)]
    public void TemporaryInputList_IsDeletedAfterReading(string option, bool kept)
    {
        var list = Path.GetTempFileName();
        try
        {
            File.WriteAllLines(list, [@"C:\x\1.mp4"]);
            var command = CommandLine.Parse(["--preset", "mkv", option, list], Cwd);

            Assert.Equal(CommandKind.Convert, command.Kind);
            Assert.Single(command.Files);
            Assert.Equal(kept, File.Exists(list));
        }
        finally
        {
            File.Delete(list);
        }
    }

    [Theory]
    [InlineData(new[] { "a.mov" }, nameof(Strings.Cli_MissingPreset))]
    [InlineData(new[] { "--preset", "mp4" }, nameof(Strings.Cli_NoFiles))]
    [InlineData(new[] { "--preset" }, nameof(Strings.Cli_UnknownOption))]
    [InlineData(new[] { "--bogus", "a.mov" }, nameof(Strings.Cli_UnknownOption))]
    public void InvalidInput_ExplainsWhy(string[] args, string message)
    {
        var command = CommandLine.Parse(args, Cwd);

        // The text before the first placeholder, in the UI language of the machine running the tests.
        var expected = Strings.ResourceManager.GetString(message, Strings.Culture)!.Split("{0}")[0];
        Assert.Equal(CommandKind.Invalid, command.Kind);
        Assert.StartsWith(expected, command.Error);
    }

    [Theory]
    [InlineData("--list-presets", CommandKind.ListPresets)]
    [InlineData("--register-shell", CommandKind.RegisterShell)]
    [InlineData("--unregister-shell", CommandKind.UnregisterShell)]
    public void Actions_AreRecognized(string arg, CommandKind kind)
    {
        Assert.Equal(kind, CommandLine.Parse([arg], Cwd).Kind);
    }

    [Fact]
    public void ToArguments_RoundTrips()
    {
        var command = CommandLine.Parse(["--preset", "mp3", "a.wav", "b.wav"], Cwd);

        Assert.Equal(command.Files, CommandLine.Parse(command.ToArguments(), "C:\\elsewhere").Files);
    }

    [Theory]
    [InlineData("rename", CommandKind.Rename)]
    [InlineData("watermark", CommandKind.Watermark)]
    [InlineData("trim", CommandKind.Trim)]
    [InlineData("pdf-protect", CommandKind.ProtectPdf)]
    [InlineData("pdf-unprotect", CommandKind.UnprotectPdf)]
    public void ToolIds_OpenTheirWindow_WithTheFiles(string id, CommandKind kind)
    {
        var command = CommandLine.Parse(["--preset", id, "a.jpg", "b.pdf"], Cwd);

        Assert.Equal(kind, command.Kind);
        Assert.Equal([Path.Combine(Cwd, "a.jpg"), Path.Combine(Cwd, "b.pdf")], command.Files);
    }

    [Fact]
    public void ToolIds_AreNotPresetIds()
    {
        Assert.All(MenuTools.All, tool => Assert.Null(Core.PresetCatalog.Find(Core.PresetCatalog.Default, tool.Id)));
    }
}
