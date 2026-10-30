namespace RoyConvert.Core;

/// <summary>An office suite that exports documents; <see cref="PdfConverter"/> picks one per file.</summary>
public interface IDocumentConverter : IConverter
{
    /// <summary>Shown in error messages, e.g. "LibreOffice".</summary>
    string Name { get; }

    /// <summary>Lower values are tried first; null when this converter cannot turn the file into <paramref name="output"/>.</summary>
    int? Priority(string input, OutputFormat output);
}
