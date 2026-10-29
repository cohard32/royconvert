using ImageMagick;
using PdfSharp.Pdf.IO;

namespace RoyConvert.Core.Tests;

public sealed class PdfSecurityTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("royconvert-security-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string Pdf(string name = "belge.pdf")
    {
        var path = Path.Combine(_dir, name);
        using var pages = new MagickImageCollection();
        pages.Add(new MagickImage(MagickColors.SkyBlue, 300, 200));
        pages.Add(new MagickImage(MagickColors.White, 300, 200));
        pages.Write(path, MagickFormat.Pdf);
        return path;
    }

    private string Out(string name) => Path.Combine(_dir, name);

    [Fact]
    public void Protect_AsksForThePassword_AndKeepsThePages()
    {
        var input = Pdf();
        var before = File.ReadAllBytes(input);

        PdfSecurity.Protect(input, Out("sifreli.pdf"), "Gizli şifre 1");

        Assert.Equal(before, File.ReadAllBytes(input));
        var locked = Assert.Throws<PdfiumException>(() => PdfiumDocument.Open(Out("sifreli.pdf")).Dispose());
        Assert.True(locked.IsPasswordProtected);
        Assert.Throws<PdfiumException>(() => PdfiumDocument.Open(Out("sifreli.pdf"), "yanlış").Dispose());
        using var opened = PdfiumDocument.Open(Out("sifreli.pdf"), "Gizli şifre 1");
        Assert.Equal(2, opened.PageCount);
        Assert.True(opened.IsEncrypted);
    }

    [Fact]
    public void Unprotect_WithThePassword_GivesAPlainPdf()
    {
        PdfSecurity.Protect(Pdf(), Out("sifreli.pdf"), "1234");

        PdfSecurity.Unprotect(Out("sifreli.pdf"), Out("acik.pdf"), "1234");

        using var plain = PdfiumDocument.Open(Out("acik.pdf"));
        Assert.False(plain.IsEncrypted);
        Assert.Equal(2, plain.PageCount);
        // Other programs read it too.
        using var sharp = PdfReader.Open(Out("acik.pdf"), PdfDocumentOpenMode.Import);
        Assert.Equal(2, sharp.PageCount);
    }

    [Fact]
    public void Unprotect_WrongOrMissingPassword_IsWrongPassword_WithoutOutput()
    {
        PdfSecurity.Protect(Pdf(), Out("sifreli.pdf"), "1234");

        foreach (var password in new[] { "4321", null })
        {
            var error = Assert.Throws<PdfPasswordException>(() => PdfSecurity.Unprotect(Out("sifreli.pdf"), Out("acik.pdf"), password));
            Assert.Equal(PdfPasswordProblem.WrongPassword, error.Problem);
            Assert.False(File.Exists(Out("acik.pdf")));
        }
    }

    [Fact]
    public void Unprotect_OwnerOnlyLimits_NeedNoPassword()
    {
        // Opens without a password but forbids printing and copying (owner password only).
        var input = Pdf();
        using (var document = PdfReader.Open(input, PdfDocumentOpenMode.Modify))
        {
            document.SecuritySettings.OwnerPassword = "sahip";
            document.SecuritySettings.PermitPrint = false;
            document.SecuritySettings.PermitExtractContent = false;
            document.SecurityHandler.SetEncryptionToV5();
            document.Save(Out("kisitli.pdf"));
        }

        PdfSecurity.Unprotect(Out("kisitli.pdf"), Out("acik.pdf"), null);

        using var plain = PdfiumDocument.Open(Out("acik.pdf"));
        Assert.False(plain.IsEncrypted);
    }

    [Fact]
    public void Unprotect_PlainPdf_IsNotProtected()
    {
        var error = Assert.Throws<PdfPasswordException>(() => PdfSecurity.Unprotect(Pdf(), Out("acik.pdf"), null));

        Assert.Equal(PdfPasswordProblem.NotProtected, error.Problem);
    }

    [Fact]
    public void Protect_ProtectedPdf_IsAlreadyProtected()
    {
        PdfSecurity.Protect(Pdf(), Out("sifreli.pdf"), "1234");

        var error = Assert.Throws<PdfPasswordException>(() => PdfSecurity.Protect(Out("sifreli.pdf"), Out("iki.pdf"), "5678"));

        Assert.Equal(PdfPasswordProblem.AlreadyProtected, error.Problem);
    }

    [Fact]
    public void BrokenPdf_IsAConversionError()
    {
        File.WriteAllText(Out("bozuk.pdf"), "not a pdf");

        Assert.Throws<ConversionException>(() => PdfSecurity.Protect(Out("bozuk.pdf"), Out("a.pdf"), "1"));
        Assert.Throws<ConversionException>(() => PdfSecurity.Unprotect(Out("bozuk.pdf"), Out("b.pdf"), "1"));
    }
}
