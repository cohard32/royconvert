namespace RoyConvert.Core;

/// <summary>
/// The "PDF" preset accepts both images and office documents: images go to <paramref name="image"/>, documents to
/// the installed office suites (<paramref name="documents"/>, by <see cref="IDocumentConverter.Priority"/>). When one
/// fails the next is tried, e.g. LibreOffice after Microsoft Office.
/// </summary>
public sealed class PdfConverter(IConverter image, IReadOnlyList<IDocumentConverter> documents) : IConverter
{
    public async Task ConvertAsync(string input, string output, Preset preset, IProgress<double?> progress, CancellationToken cancellationToken)
    {
        if (Formats.ImageInputs.Contains(Formats.ExtensionOf(input)))
        {
            await image.ConvertAsync(input, output, preset, progress, cancellationToken);
            return;
        }

        var candidates = documents
            .Select(converter => (Converter: converter, Priority: converter.Priority(input)))
            .Where(c => c.Priority is not null)
            .OrderBy(c => c.Priority)
            .Select(c => c.Converter)
            .ToList();
        if (candidates.Count == 0)
        {
            throw new ConversionException(
                "Neither Microsoft Office nor LibreOffice was found. Install one of them to convert documents " +
                "(LibreOffice is free: https://www.libreoffice.org).");
        }

        var errors = new List<string>();
        foreach (var converter in candidates)
        {
            try
            {
                await converter.ConvertAsync(input, output, preset, progress, cancellationToken);
                return;
            }
            catch (ConversionException e)
            {
                errors.Add(candidates.Count == 1 ? e.Message : $"{converter.Name}: {e.Message}");
            }
        }

        throw new ConversionException(string.Join(Environment.NewLine, errors));
    }

    /// <summary>Only images can be combined (the preset accepts nothing else).</summary>
    public Task CombineAsync(IReadOnlyList<string> inputs, string output, Preset preset, IProgress<double?> progress, CancellationToken cancellationToken) =>
        image.CombineAsync(inputs, output, preset, progress, cancellationToken);
}
