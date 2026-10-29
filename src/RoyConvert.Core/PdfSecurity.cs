using PdfSharp.Pdf.IO;

namespace RoyConvert.Core;

public enum PdfPasswordProblem
{
    /// <summary>The PDF needs a password to open and the one given is missing or wrong.</summary>
    WrongPassword,
    /// <summary>Removing: the PDF has no password.</summary>
    NotProtected,
    /// <summary>Adding: the PDF already has a password; remove it first.</summary>
    AlreadyProtected,
}

public sealed class PdfPasswordException(PdfPasswordProblem problem, string message) : Exception(message)
{
    public PdfPasswordProblem Problem { get; } = problem;
}

/// <summary>Puts a password on a PDF or takes it off; the result is a new file, the input stays as it is.</summary>
public static class PdfSecurity
{
    /// <summary>
    /// The new PDF asks for <paramref name="password"/> to be opened (AES-256, which current PDF readers support).
    /// The same password is the owner password, so whoever opens it may also print, copy and edit.
    /// </summary>
    /// <exception cref="PdfPasswordException">The PDF already has a password.</exception>
    /// <exception cref="ConversionException">The PDF cannot be read or written.</exception>
    public static void Protect(string input, string output, string password)
    {
        if (string.IsNullOrEmpty(password))
        {
            throw new ArgumentException("A password is needed.", nameof(password));
        }

        try
        {
            using var check = OpenWithPdfium(input, null);
            if (check.IsEncrypted)
            {
                throw new PdfPasswordException(PdfPasswordProblem.AlreadyProtected, "The PDF already has a password.");
            }
        }
        catch (PdfPasswordException e) when (e.Problem == PdfPasswordProblem.WrongPassword)
        {
            // It asks for a password to open: protected already.
            throw new PdfPasswordException(PdfPasswordProblem.AlreadyProtected, "The PDF already has a password.");
        }

        try
        {
            using var document = PdfReader.Open(input, PdfDocumentOpenMode.Modify);
            document.SecuritySettings.UserPassword = password;
            document.SecuritySettings.OwnerPassword = password;
            document.SecurityHandler.SetEncryptionToV5();
            document.Save(output);
        }
        catch (Exception e) when (e is PdfReaderException or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            DeleteQuietly(output);
            throw new ConversionException(e.Message, e);
        }
    }

    /// <summary>
    /// The new PDF has no password and no limits on printing, copying or editing. <paramref name="password"/> is needed
    /// only for a PDF that asks for one to be opened.
    /// </summary>
    /// <exception cref="PdfPasswordException">The password is wrong, or the PDF has none.</exception>
    /// <exception cref="ConversionException">The PDF cannot be read or written.</exception>
    public static void Unprotect(string input, string output, string? password)
    {
        using var document = OpenWithPdfium(input, string.IsNullOrEmpty(password) ? null : password);
        if (!document.IsEncrypted)
        {
            throw new PdfPasswordException(PdfPasswordProblem.NotProtected, "The PDF has no password.");
        }

        try
        {
            File.WriteAllBytes(output, document.SaveWithoutSecurity());
        }
        catch (Exception e) when (e is PdfiumException or IOException or UnauthorizedAccessException)
        {
            DeleteQuietly(output);
            throw new ConversionException(e.Message, e);
        }
    }

    private static PdfiumDocument OpenWithPdfium(string input, string? password)
    {
        try
        {
            return PdfiumDocument.Open(input, password);
        }
        catch (PdfiumException e) when (e.IsPasswordProtected)
        {
            throw new PdfPasswordException(PdfPasswordProblem.WrongPassword, "The password is wrong.");
        }
        catch (Exception e) when (e is PdfiumException or IOException or UnauthorizedAccessException)
        {
            throw new ConversionException(e.Message, e);
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
            // Nothing usable was written either way.
        }
    }
}
