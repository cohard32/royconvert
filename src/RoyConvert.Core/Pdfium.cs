using System.Runtime.InteropServices;

namespace RoyConvert.Core;

/// <summary>PDFium could not open a document; <see cref="IsPasswordProtected"/> tells why when it knows.</summary>
public sealed class PdfiumException(string message, bool isPasswordProtected = false) : Exception(message)
{
    public bool IsPasswordProtected { get; } = isPasswordProtected;
}

/// <summary>
/// A PDF opened with PDFium (the pdfium.dll that the Docnet.Core package ships), called directly.
/// </summary>
/// <remarks>
/// Docnet itself is not used: its DocReader sets up a form-fill environment with a structure that .NET marshals as a
/// temporary copy, which PDFium keeps a pointer to. When the memory had been reused, closing the document crashed the
/// whole process (access violation in FPDFDOC_ExitFormFillEnvironment), more often the more the garbage collector
/// ran, e.g. with several PDFs converted at once. Here no form environment is created (form fields are drawn from
/// their appearance streams with FPDF_ANNOT), the document's bytes stay in native memory until it is closed, and every
/// PDFium call goes through one lock because PDFium is not thread safe.
/// </remarks>
public sealed class PdfiumDocument : IDisposable
{
    private static readonly object Lock = new();
    private static bool _initialized;

    private readonly IntPtr _data;
    private IntPtr _document;

    private PdfiumDocument(IntPtr data, IntPtr document)
    {
        _data = data;
        _document = document;
    }

    /// <exception cref="PdfiumException">The bytes are not a PDF PDFium can open (damaged or password protected).</exception>
    public static PdfiumDocument Open(byte[] bytes)
    {
        var data = Marshal.AllocHGlobal(Math.Max(bytes.Length, 1));
        Marshal.Copy(bytes, 0, data, bytes.Length);
        lock (Lock)
        {
            Initialize();
            var document = FPDF_LoadMemDocument(data, bytes.Length, null);
            if (document != IntPtr.Zero)
            {
                return new PdfiumDocument(data, document);
            }

            var error = FPDF_GetLastError();
            Marshal.FreeHGlobal(data);
            throw new PdfiumException(
                error == FPDF_ERR_PASSWORD ? "The PDF is password protected." : $"The PDF could not be opened (PDFium error {error}).",
                error == FPDF_ERR_PASSWORD);
        }
    }

    public static PdfiumDocument Open(string path) => Open(File.ReadAllBytes(path));

    public int PageCount
    {
        get
        {
            lock (Lock)
            {
                return FPDF_GetPageCount(Handle);
            }
        }
    }

    /// <summary>Page size in points (1/72 inch) as displayed, i.e. with the page's rotation applied.</summary>
    public (double Width, double Height) PageSize(int index)
    {
        lock (Lock)
        {
            return FPDF_GetPageSizeByIndex(Handle, index, out var width, out var height) != 0
                ? (width, height)
                : throw new PdfiumException($"Page {index + 1} could not be read.");
        }
    }

    /// <summary>The page drawn on white at <paramref name="width"/> × <paramref name="height"/> pixels, as BGRA bytes.</summary>
    public byte[] Render(int index, int width, int height)
    {
        lock (Lock)
        {
            var page = LoadPage(index);
            var bitmap = FPDFBitmap_Create(width, height, 1);
            try
            {
                if (bitmap == IntPtr.Zero)
                {
                    throw new PdfiumException($"Page {index + 1} is too large to render at {width} × {height} pixels.");
                }

                FPDFBitmap_FillRect(bitmap, 0, 0, width, height, 0xFFFFFFFF);
                FPDF_RenderPageBitmap(bitmap, page, 0, 0, width, height, 0, FPDF_ANNOT);
                var stride = FPDFBitmap_GetStride(bitmap);
                var buffer = FPDFBitmap_GetBuffer(bitmap);
                var pixels = new byte[width * height * 4];
                for (var row = 0; row < height; row++)
                {
                    Marshal.Copy(buffer + row * stride, pixels, row * width * 4, width * 4);
                }

                return pixels;
            }
            finally
            {
                if (bitmap != IntPtr.Zero)
                {
                    FPDFBitmap_Destroy(bitmap);
                }

                FPDF_ClosePage(page);
            }
        }
    }

    /// <summary>The page's text, as PDFium extracts it.</summary>
    public string Text(int index)
    {
        lock (Lock)
        {
            var page = LoadPage(index);
            var text = FPDFText_LoadPage(page);
            try
            {
                var count = FPDFText_CountChars(text);
                if (count <= 0)
                {
                    return string.Empty;
                }

                // UTF-16 with a terminating zero.
                var buffer = new ushort[count + 1];
                var written = FPDFText_GetText(text, 0, count, buffer);
                return new string(buffer.Take(Math.Max(written - 1, 0)).Select(c => (char)c).ToArray());
            }
            finally
            {
                if (text != IntPtr.Zero)
                {
                    FPDFText_ClosePage(text);
                }

                FPDF_ClosePage(page);
            }
        }
    }

    /// <summary>A new PDF with all pages of <paramref name="sources"/>, in order, copied as they are.</summary>
    public static byte[] Combine(IEnumerable<(PdfiumDocument Document, int First, int Last)> sources)
    {
        lock (Lock)
        {
            Initialize();
            var target = FPDF_CreateNewDocument();
            try
            {
                var pages = 0;
                foreach (var (document, first, last) in sources)
                {
                    if (FPDF_ImportPages(target, document.Handle, $"{first + 1}-{last + 1}", pages) == 0)
                    {
                        throw new PdfiumException("The pages could not be copied.");
                    }

                    pages += last - first + 1;
                }

                return Save(target);
            }
            finally
            {
                FPDF_CloseDocument(target);
            }
        }
    }

    public void Dispose()
    {
        lock (Lock)
        {
            if (_document == IntPtr.Zero)
            {
                return;
            }

            FPDF_CloseDocument(_document);
            _document = IntPtr.Zero;
        }

        Marshal.FreeHGlobal(_data);
    }

    private IntPtr Handle => _document != IntPtr.Zero ? _document : throw new ObjectDisposedException(nameof(PdfiumDocument));

    private IntPtr LoadPage(int index)
    {
        var page = FPDF_LoadPage(Handle, index);
        return page != IntPtr.Zero ? page : throw new PdfiumException($"Page {index + 1} could not be read.");
    }

    private static void Initialize()
    {
        if (!_initialized)
        {
            FPDF_InitLibrary();
            _initialized = true;
        }
    }

    private static byte[] Save(IntPtr document)
    {
        using var output = new MemoryStream();
        WriteBlock write = (_, data, size) =>
        {
            var chunk = new byte[size];
            Marshal.Copy(data, chunk, 0, (int)size);
            output.Write(chunk);
            return 1;
        };
        var writer = new FileWrite { Version = 1, WriteBlock = Marshal.GetFunctionPointerForDelegate(write) };
        var saved = FPDF_SaveAsCopy(document, ref writer, 0);
        GC.KeepAlive(write);
        return saved != 0 ? output.ToArray() : throw new PdfiumException("The PDF could not be written.");
    }

    private const uint FPDF_ERR_PASSWORD = 4;
    private const int FPDF_ANNOT = 0x01;
    private const string Library = "pdfium";

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int WriteBlock(IntPtr self, IntPtr data, uint size);

    [StructLayout(LayoutKind.Sequential)]
    private struct FileWrite
    {
        public int Version;
        public IntPtr WriteBlock;
    }

    [DllImport(Library)] private static extern void FPDF_InitLibrary();
    [DllImport(Library)] private static extern IntPtr FPDF_LoadMemDocument(IntPtr data, int size, [MarshalAs(UnmanagedType.LPStr)] string? password);
    [DllImport(Library)] private static extern uint FPDF_GetLastError();
    [DllImport(Library)] private static extern int FPDF_GetPageCount(IntPtr document);
    [DllImport(Library)] private static extern int FPDF_GetPageSizeByIndex(IntPtr document, int index, out double width, out double height);
    [DllImport(Library)] private static extern IntPtr FPDF_LoadPage(IntPtr document, int index);
    [DllImport(Library)] private static extern void FPDF_ClosePage(IntPtr page);
    [DllImport(Library)] private static extern void FPDF_CloseDocument(IntPtr document);
    [DllImport(Library)] private static extern IntPtr FPDFBitmap_Create(int width, int height, int alpha);
    [DllImport(Library)] private static extern void FPDFBitmap_FillRect(IntPtr bitmap, int left, int top, int width, int height, uint color);
    [DllImport(Library)] private static extern void FPDF_RenderPageBitmap(IntPtr bitmap, IntPtr page, int x, int y, int width, int height, int rotate, int flags);
    [DllImport(Library)] private static extern IntPtr FPDFBitmap_GetBuffer(IntPtr bitmap);
    [DllImport(Library)] private static extern int FPDFBitmap_GetStride(IntPtr bitmap);
    [DllImport(Library)] private static extern void FPDFBitmap_Destroy(IntPtr bitmap);
    [DllImport(Library)] private static extern IntPtr FPDF_CreateNewDocument();
    [DllImport(Library)] private static extern int FPDF_ImportPages(IntPtr target, IntPtr source, [MarshalAs(UnmanagedType.LPStr)] string pages, int index);
    [DllImport(Library)] private static extern int FPDF_SaveAsCopy(IntPtr document, ref FileWrite writer, uint flags);
    [DllImport(Library)] private static extern IntPtr FPDFText_LoadPage(IntPtr page);
    [DllImport(Library)] private static extern int FPDFText_CountChars(IntPtr text);
    [DllImport(Library)] private static extern int FPDFText_GetText(IntPtr text, int start, int count, [Out] ushort[] buffer);
    [DllImport(Library)] private static extern void FPDFText_ClosePage(IntPtr text);
}
