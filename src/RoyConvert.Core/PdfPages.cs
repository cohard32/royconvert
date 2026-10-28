using RoyConvert.Core.Imaging;

namespace RoyConvert.Core;

/// <summary>
/// Merges and splits PDFs with PDFium. Pages are copied as they are (text, fonts and vector graphics are kept),
/// never rendered to images.
/// </summary>
public static class PdfPages
{
    /// <summary>Writes the pages of all inputs, in order, to one PDF.</summary>
    public static void Merge(IReadOnlyList<string> inputs, string output, IProgress<double?> progress, CancellationToken cancellationToken)
    {
        var documents = new List<PdfiumDocument>(inputs.Count);
        try
        {
            foreach (var input in inputs)
            {
                cancellationToken.ThrowIfCancellationRequested();
                documents.Add(Open(input));
            }

            cancellationToken.ThrowIfCancellationRequested();
            progress.Report(null);
            // The pages go into a new document, so no input has to accept them.
            File.WriteAllBytes(output, PdfiumDocument.Combine(documents.Select(d => (d, 0, d.PageCount - 1))));
        }
        catch (PdfiumException e)
        {
            DeleteQuietly(output);
            throw new ConversionException("The PDFs could not be merged: " + e.Message, e);
        }
        finally
        {
            documents.ForEach(d => d.Dispose());
        }
    }

    /// <summary>
    /// Writes every page to its own PDF named after <paramref name="output"/> ("name-01.pdf", "name-02.pdf"…, see
    /// <see cref="ImageConverter.PagePaths"/>). Nothing is left behind on failure or cancellation.
    /// </summary>
    public static void Split(string input, string output, IProgress<double?> progress, CancellationToken cancellationToken)
    {
        using var document = Open(input);
        var pageCount = document.PageCount;
        if (pageCount < 2)
        {
            throw new ConversionException("The PDF has only one page, so there is nothing to split.");
        }

        var paths = ImageConverter.PagePaths(output, pageCount);
        var written = new List<string>();
        try
        {
            for (var i = 0; i < pageCount; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var page = PdfiumDocument.Combine([(document, i, i)]);
                written.Add(paths[i]);
                File.WriteAllBytes(paths[i], page);
                progress.Report((i + 1.0) / pageCount);
            }
        }
        catch (Exception e)
        {
            written.ForEach(DeleteQuietly);
            throw e is PdfiumException ? new ConversionException("The PDF could not be split: " + e.Message, e) : e;
        }
    }

    /// <summary>
    /// Turns every page by <paramref name="rotation"/> (clockwise) on top of its own rotation, with PDFsharp: only the
    /// pages' /Rotate entries change, the content stays as it is.
    /// </summary>
    public static void Rotate(string input, string output, Rotation rotation, IProgress<double?> progress, CancellationToken cancellationToken)
    {
        // PDFium names a damaged or protected file clearly; PDFsharp throws whatever its parser hits.
        Open(input).Dispose();
        progress.Report(null);
        try
        {
            using var document = PdfSharp.Pdf.IO.PdfReader.Open(input, PdfSharp.Pdf.IO.PdfDocumentOpenMode.Modify);
            foreach (var page in document.Pages)
            {
                cancellationToken.ThrowIfCancellationRequested();
                page.Rotate = ((page.Rotate + (int)rotation) % 360 + 360) % 360;
            }

            cancellationToken.ThrowIfCancellationRequested();
            document.Save(output);
        }
        catch (Exception e) when (e is not (OperationCanceledException or IOException or UnauthorizedAccessException))
        {
            DeleteQuietly(output);
            throw new ConversionException(
                $"{Path.GetFileName(input)} could not be rotated. It may be damaged or password protected. ({e.Message})", e);
        }
        catch
        {
            DeleteQuietly(output);
            throw;
        }

        progress.Report(1);
    }

    /// <summary>Opens the PDF with PDFium, naming the file in the error if it cannot.</summary>
    internal static PdfiumDocument Open(string path)
    {
        try
        {
            return PdfiumDocument.Open(path);
        }
        catch (PdfiumException e)
        {
            throw new ConversionException(
                $"{Path.GetFileName(path)} could not be read. It may be damaged or password protected.", e);
        }
    }

    private static void DeleteQuietly(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
