using System.Globalization;

namespace RoyConvert.Core;

public enum PageRangeProblem
{
    None,
    /// <summary>Nothing typed.</summary>
    Empty,
    /// <summary>Not like "1-3, 5, 8-".</summary>
    Invalid,
    /// <summary>A page after the last one (see <see cref="PageRange.FirstMissingPage"/>).</summary>
    PastTheEnd,
    /// <summary>Deleting would leave no page.</summary>
    NothingLeft,
}

/// <param name="Pages">0-based page indexes in document order, each once.</param>
/// <param name="FirstMissingPage">1-based, for <see cref="PageRangeProblem.PastTheEnd"/>.</param>
public sealed record PageRange(IReadOnlyList<int> Pages, PageRangeProblem Problem = PageRangeProblem.None, int FirstMissingPage = 0)
{
    public bool IsValid => Problem == PageRangeProblem.None;
}

/// <summary>
/// Keeps or deletes some pages of a PDF into a new PDF next to it, with PDFium: the pages are copied as they are
/// (text, fonts and vector graphics are kept), the input is not changed.
/// </summary>
public static class PdfPageSelection
{
    /// <summary>
    /// Reads "1-3, 5, 8-" ("8-": to the last page; ';' and spaces separate too) for a PDF of
    /// <paramref name="pageCount"/> pages. With <paramref name="delete"/> the result is the pages that are left.
    /// </summary>
    public static PageRange Parse(string text, int pageCount, bool delete)
    {
        // "1 - 3" is one range, not three parts.
        var joined = System.Text.RegularExpressions.Regex.Replace(text, @"\s*[-–]\s*", "-");
        var parts = joined.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            return new PageRange([], PageRangeProblem.Empty);
        }

        var chosen = new SortedSet<int>();
        foreach (var part in parts)
        {
            var dash = part.IndexOfAny(['-', '–']);
            int first, last;
            if (dash < 0)
            {
                if (!TryPage(part, out first))
                {
                    return new PageRange([], PageRangeProblem.Invalid);
                }

                last = first;
            }
            else
            {
                var end = part[(dash + 1)..];
                if (!TryPage(part[..dash], out first) || (end.Length > 0 && !TryPage(end, out _)))
                {
                    return new PageRange([], PageRangeProblem.Invalid);
                }

                last = end.Length == 0 ? Math.Max(first, pageCount) : int.Parse(end, CultureInfo.InvariantCulture);
                if (last < first)
                {
                    return new PageRange([], PageRangeProblem.Invalid);
                }
            }

            if (first > pageCount || last > pageCount)
            {
                return new PageRange([], PageRangeProblem.PastTheEnd, Math.Max(first, pageCount + 1));
            }

            for (var page = first; page <= last; page++)
            {
                chosen.Add(page - 1);
            }
        }

        var pages = delete ? Enumerable.Range(0, pageCount).Where(p => !chosen.Contains(p)).ToList() : chosen.ToList();
        return delete && pages.Count == 0 ? new PageRange([], PageRangeProblem.NothingLeft) : new PageRange(pages);
    }

    private static bool TryPage(string text, out int page) =>
        int.TryParse(text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out page) && page >= 1;

    /// <exception cref="ConversionException">The PDF cannot be read (damaged or password protected).</exception>
    public static int PageCount(string input)
    {
        using var document = PdfPages.Open(input);
        return document.PageCount;
    }

    /// <summary>Writes the chosen pages of <paramref name="input"/> to <paramref name="output"/>, in document order.</summary>
    /// <param name="ranges">As typed, see <see cref="Parse"/>; each PDF is checked against its own page count.</param>
    /// <exception cref="ConversionException">The PDF cannot be read or written, or the pages do not fit it.</exception>
    public static void Apply(string input, string output, string ranges, bool delete)
    {
        using var document = PdfPages.Open(input);
        var range = Parse(ranges, document.PageCount, delete);
        if (!range.IsValid)
        {
            throw new ConversionException(range.Problem == PageRangeProblem.PastTheEnd
                ? $"The PDF has only {document.PageCount} pages."
                : $"The pages do not fit this PDF ({range.Problem}).");
        }

        try
        {
            File.WriteAllBytes(output, PdfiumDocument.Combine(Runs(range.Pages).Select(r => (document, r.First, r.Last))));
        }
        catch (Exception e) when (e is PdfiumException or IOException or UnauthorizedAccessException)
        {
            try
            {
                File.Delete(output);
            }
            catch (IOException)
            {
                // Nothing usable was written either way.
            }

            throw new ConversionException("The pages could not be written: " + e.Message, e);
        }
    }

    /// <summary>Sorted pages as runs of consecutive pages: [0,1,2,5] → (0,2), (5,5).</summary>
    internal static IEnumerable<(int First, int Last)> Runs(IReadOnlyList<int> pages)
    {
        for (var i = 0; i < pages.Count;)
        {
            var j = i;
            while (j + 1 < pages.Count && pages[j + 1] == pages[j] + 1)
            {
                j++;
            }

            yield return (pages[i], pages[j]);
            i = j + 1;
        }
    }
}
