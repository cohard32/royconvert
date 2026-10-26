namespace RoyConvert.Core;

/// <param name="Id">Stable identifier passed on the command line, e.g. "video/mp4-720p".</param>
/// <param name="Name">Menu path; "/" separates submenus, e.g. "720p/MP4".</param>
/// <param name="FileNameTemplate">See <see cref="OutputPath"/> for the supported tokens; filled from the first input.</param>
/// <param name="CombinesInputs">All selected files become one job and one output (e.g. images → one PDF).</param>
public sealed record Preset(
    string Id,
    string Name,
    OutputFormat Output,
    IReadOnlySet<string> InputExtensions,
    ConversionSettings Settings,
    string FileNameTemplate = OutputPath.DefaultTemplate,
    bool CombinesInputs = false)
{
    public bool Accepts(string path) => InputExtensions.Contains(Formats.ExtensionOf(path));
}
