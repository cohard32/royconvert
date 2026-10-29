namespace RoyConvert.Core;

/// <param name="Id">Stable identifier passed on the command line, e.g. "video/mp4-720p".</param>
/// <param name="Name">Menu path; "/" separates submenus, e.g. "720p/MP4".</param>
/// <param name="FileNameTemplate">See <see cref="OutputPath"/> for the supported tokens; filled from the first input.</param>
/// <param name="CombinesInputs">All selected files become one job and one output (e.g. images → one PDF).</param>
/// <param name="SplitsPages">Every page of the PDF input becomes its own PDF ("name-01.pdf", "name-02.pdf"…).</param>
/// <param name="ReadsDvd">The input is a DVD folder or drive (see <see cref="Dvd.DvdDisc"/>) instead of a file.</param>
/// <param name="CompressesPdf">The PDF input is made smaller by recompressing its images (see <see cref="PdfCompression"/>).</param>
public sealed record Preset(
    string Id,
    string Name,
    OutputFormat Output,
    IReadOnlySet<string> InputExtensions,
    ConversionSettings Settings,
    string FileNameTemplate = OutputPath.DefaultTemplate,
    bool CombinesInputs = false,
    bool SplitsPages = false,
    bool ReadsDvd = false,
    bool CompressesPdf = false)
{
    public bool Accepts(string path) => ReadsDvd
        ? Dvd.DvdDisc.Open(path) is not null
        : InputExtensions.Contains(Formats.ExtensionOf(path));
}
