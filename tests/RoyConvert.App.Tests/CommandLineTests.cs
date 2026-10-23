namespace RoyConvert.App.Tests;

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
    [InlineData(new[] { "a.mov" }, "Missing --preset")]
    [InlineData(new[] { "--preset", "mp4" }, "No input files")]
    [InlineData(new[] { "--preset" }, "Unknown or incomplete option")]
    [InlineData(new[] { "--bogus", "a.mov" }, "Unknown or incomplete option")]
    public void InvalidInput_ExplainsWhy(string[] args, string expected)
    {
        var command = CommandLine.Parse(args, Cwd);

        Assert.Equal(CommandKind.Invalid, command.Kind);
        Assert.Contains(expected, command.Error);
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
}
