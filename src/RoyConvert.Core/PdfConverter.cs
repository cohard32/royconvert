namespace RoyConvert.Core;

/// <summary>
/// The "PDF" preset accepts both images and office documents: images go to <paramref name="image"/>, documents to
/// the installed office suites (<paramref name="documents"/>, by <see cref="IDocumentConverter.Priority"/>). When one
/// fails the next is tried, e.g. LibreOffice after Microsoft Office. It also merges and splits PDFs (<see cref="PdfPages"/>).
/// </summary>
public sealed class PdfConverter(IConverter image, IReadOnlyList<IDocumentConverter> documents) : IConverter
{
    public async Task ConvertAsync(string input, string output, Preset preset, IProgress<double?> progress, CancellationToken cancellationToken)
    {
        if (preset.SplitsPages)
        {
            await Task.Run(() => PdfPages.Split(input, output, progress, cancellationToken), cancellationToken);
            return;
        }

        if (Formats.ExtensionOf(input) == "pdf" && preset.Settings.Rotation != Rotation.None)
        {
            await Task.Run(() => PdfPages.Rotate(input, output, preset.Settings.Rotation, progress, cancellationToken), cancellationToken);
            return;
        }

        if (IsImage(input))
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

    /// <summary>
    /// Images and PDFs in the given order. Images only are written by <paramref name="image"/> in one go; otherwise
    /// each image becomes a one-page PDF first and the PDFs' own pages are copied unchanged.
    /// </summary>
    public async Task CombineAsync(IReadOnlyList<string> inputs, string output, Preset preset, IProgress<double?> progress, CancellationToken cancellationToken)
    {
        if (inputs.All(IsImage))
        {
            await image.CombineAsync(inputs, output, preset, progress, cancellationToken);
            return;
        }

        progress.Report(0);
        var work = Directory.CreateTempSubdirectory("royconvert-merge-").FullName;
        try
        {
            var parts = new List<string>(inputs.Count);
            var noProgress = new Progress<double?>();
            for (var i = 0; i < inputs.Count; i++)
            {
                if (IsImage(inputs[i]))
                {
                    var page = Path.Combine(work, $"{i}.pdf");
                    await image.ConvertAsync(inputs[i], page, preset, noProgress, cancellationToken);
                    parts.Add(page);
                }
                else
                {
                    parts.Add(inputs[i]);
                }

                progress.Report((i + 1.0) / inputs.Count / 2);
            }

            await Task.Run(() => PdfPages.Merge(parts, output, progress, cancellationToken), cancellationToken);
        }
        finally
        {
            try
            {
                Directory.Delete(work, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        progress.Report(1);
    }

    private static bool IsImage(string path) => Formats.ImageInputs.Contains(Formats.ExtensionOf(path));
}
