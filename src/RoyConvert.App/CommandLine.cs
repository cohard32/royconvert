using RoyConvert.App.Resources;

namespace RoyConvert.App;

public enum CommandKind
{
    /// <summary>No arguments: show the home window.</summary>
    Home,
    Convert,
    ListPresets,
    RegisterShell,
    UnregisterShell,
    Invalid,
}

/// <summary>
/// Usage:
///   royconvert
///   royconvert --preset &lt;id&gt; [--input-list &lt;file&gt;] [files...]
///   royconvert --list-presets
///   royconvert --register-shell | --unregister-shell
/// </summary>
public sealed record CommandLine(CommandKind Kind, string? PresetId, IReadOnlyList<string> Files, string? Error = null)
{
    public static CommandLine Parse(IReadOnlyList<string> args, string? workingDirectory = null)
    {
        workingDirectory ??= Environment.CurrentDirectory;
        if (args.Count == 0)
        {
            return new CommandLine(CommandKind.Home, null, []);
        }

        string? presetId = null;
        var files = new List<string>();
        for (var i = 0; i < args.Count; i++)
        {
            switch (args[i])
            {
                case "--list-presets":
                    return new CommandLine(CommandKind.ListPresets, null, []);
                case "--register-shell":
                    return new CommandLine(CommandKind.RegisterShell, null, []);
                case "--unregister-shell":
                    return new CommandLine(CommandKind.UnregisterShell, null, []);
                case "--preset" when i + 1 < args.Count:
                    presetId = args[++i];
                    break;
                case "--input-list" when i + 1 < args.Count:
                    var list = Path.GetFullPath(args[++i], workingDirectory);
                    try
                    {
                        files.AddRange(File.ReadAllLines(list).Where(line => !string.IsNullOrWhiteSpace(line)));
                    }
                    catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                    {
                        return Invalid(string.Format(Strings.Cli_InputListUnreadable, list, e.Message));
                    }

                    break;
                case var option when option.StartsWith("--", StringComparison.Ordinal):
                    return Invalid(string.Format(Strings.Cli_UnknownOption, option));
                default:
                    files.Add(args[i]);
                    break;
            }
        }

        if (presetId is null)
        {
            return Invalid(Strings.Cli_MissingPreset);
        }

        if (files.Count == 0)
        {
            return Invalid(Strings.Cli_NoFiles);
        }

        // Absolute paths, so the arguments can be forwarded to an instance with another working directory.
        var absolute = files.Select(f => Path.GetFullPath(f.Trim().Trim('"'), workingDirectory)).ToList();
        return new CommandLine(CommandKind.Convert, presetId, absolute);
    }

    /// <summary>Arguments that reproduce this command; used to forward it to the running instance.</summary>
    public IReadOnlyList<string> ToArguments() => Kind == CommandKind.Convert
        ? ["--preset", PresetId!, .. Files]
        : throw new InvalidOperationException($"{Kind} commands are not forwarded.");

    private static CommandLine Invalid(string error) => new(CommandKind.Invalid, null, [], error);
}
