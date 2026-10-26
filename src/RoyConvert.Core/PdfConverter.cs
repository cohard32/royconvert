namespace RoyConvert.Core;

/// <summary>
/// The "PDF" preset accepts both images and office documents: images go to <paramref name="image"/>,
/// everything else to LibreOffice (<paramref name="office"/>, null when it is not installed).
/// </summary>
public sealed class PdfConverter(IConverter image, IConverter? office) : IConverter
{
    public Task ConvertAsync(string input, string output, Preset preset, IProgress<double?> progress, CancellationToken cancellationToken)
    {
        if (Formats.ImageInputs.Contains(Formats.ExtensionOf(input)))
        {
            return image.ConvertAsync(input, output, preset, progress, cancellationToken);
        }

        return (office ?? throw new ConversionException(
                "LibreOffice was not found. Install it from https://www.libreoffice.org to convert documents."))
            .ConvertAsync(input, output, preset, progress, cancellationToken);
    }

    /// <summary>Only images can be combined (the preset accepts nothing else).</summary>
    public Task CombineAsync(IReadOnlyList<string> inputs, string output, Preset preset, IProgress<double?> progress, CancellationToken cancellationToken) =>
        image.CombineAsync(inputs, output, preset, progress, cancellationToken);
}
