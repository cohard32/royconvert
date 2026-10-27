namespace RoyConvert.Core;

/// <summary>An office suite that exports documents to PDF; <see cref="PdfConverter"/> picks one per file.</summary>
public interface IDocumentConverter : IConverter
{
    /// <summary>Shown in error messages, e.g. "LibreOffice".</summary>
    string Name { get; }

    /// <summary>Lower values are tried first; null when this converter cannot open the file.</summary>
    int? Priority(string input);
}
