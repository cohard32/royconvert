using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RoyConvert.App.Resources;
using RoyConvert.Core;

namespace RoyConvert.App.ViewModels;

/// <summary>Keeps or deletes the same pages of each selected PDF, into a new PDF next to it.</summary>
public partial class PdfPagesViewModel : ViewModelBase
{
    private readonly IReadOnlyList<string> _files;
    private readonly int? _firstPageCount;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    [NotifyPropertyChangedFor(nameof(Problem), nameof(Preview), nameof(Keep))]
    private bool _delete;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    [NotifyPropertyChangedFor(nameof(Problem), nameof(Preview))]
    private string _pages = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private string _status = string.Empty;

    [ObservableProperty]
    private string _errors = string.Empty;

    public PdfPagesViewModel(IEnumerable<string> files)
    {
        _files = files.Where(f => File.Exists(f) && Formats.IsPdf(f)).ToList();
        Title = string.Format(Strings.Pages_Title, _files.Count);
        if (_files.Count == 0)
        {
            Status = Strings.Pages_NoFiles;
            return;
        }

        try
        {
            _firstPageCount = PdfPageSelection.PageCount(_files[0]);
        }
        catch (ConversionException)
        {
            // Shown in the preview; saving reports it for each file.
        }
    }

    /// <summary>Raised when every file is done, or on close.</summary>
    public event Action? CloseRequested;

    public string Title { get; }

    /// <summary>Two-way with the "Keep" choice, the other side of <see cref="Delete"/>.</summary>
    public bool Keep
    {
        get => !Delete;
        set => Delete = !value;
    }

    /// <summary>The pages as typed, checked against the first PDF (each PDF is checked again when saved).</summary>
    private PageRange? Range() => _firstPageCount is { } count ? PdfPageSelection.Parse(Pages, count, Delete) : null;

    /// <summary>What is wrong with the pages as typed; empty when they can be used or nothing is typed yet.</summary>
    public string Problem => Range() switch
    {
        null or { Problem: PageRangeProblem.None or PageRangeProblem.Empty } => string.Empty,
        { Problem: PageRangeProblem.PastTheEnd } r => string.Format(Strings.Pages_PastTheEnd, _firstPageCount, r.FirstMissingPage),
        { Problem: PageRangeProblem.NothingLeft } => Strings.Pages_NothingLeft,
        _ => Strings.Pages_Invalid,
    };

    /// <summary>The first PDF's page count now and after saving.</summary>
    public string Preview
    {
        get
        {
            if (_files.Count == 0)
            {
                return string.Empty;
            }

            var name = Path.GetFileName(_files[0]);
            if (_firstPageCount is not { } count)
            {
                return string.Format(Strings.Pages_Unreadable, name);
            }

            return Range() is { IsValid: true } range
                ? string.Format(Strings.Pages_Result, name, count, range.Pages.Count)
                : string.Format(Strings.Pages_Count, name, count);
        }
    }

    // A first PDF that cannot be read does not stop the others: they are checked when saved.
    private bool CanApply() => !IsBusy && _files.Count > 0 && (_firstPageCount is null
        ? !string.IsNullOrWhiteSpace(Pages)
        : Range() is { IsValid: true });

    [RelayCommand(CanExecute = nameof(CanApply))]
    private async Task ApplyAsync()
    {
        var pages = Pages;
        var delete = Delete;
        var suffix = delete ? Strings.Pages_DeleteSuffix : Strings.Pages_KeepSuffix;
        IsBusy = true;
        Errors = string.Empty;
        var failed = new List<string>();
        for (var i = 0; i < _files.Count; i++)
        {
            Status = string.Format(Strings.Watermark_Progress, i + 1, _files.Count);
            var input = _files[i];
            try
            {
                // Each PDF has its own page count: say in the UI language why these pages do not fit it.
                var count = await Task.Run(() => PdfPageSelection.PageCount(input));
                var range = PdfPageSelection.Parse(pages, count, delete);
                if (!range.IsValid)
                {
                    failed.Add($"{Path.GetFileName(input)}: " + (range.Problem == PageRangeProblem.PastTheEnd
                        ? string.Format(Strings.Pages_PastTheEnd, count, range.FirstMissingPage)
                        : range.Problem == PageRangeProblem.NothingLeft ? Strings.Pages_NothingLeft : Strings.Pages_Invalid));
                    continue;
                }

                await Task.Run(() => PdfPageSelection.Apply(input, OutputPath.Beside(input, suffix, "pdf"), pages, delete));
            }
            catch (ConversionException e)
            {
                failed.Add($"{Path.GetFileName(input)}: {e.Message}");
            }
        }

        IsBusy = false;
        if (failed.Count == 0)
        {
            CloseRequested?.Invoke();
            return;
        }

        Status = string.Format(Strings.Pages_Failed, failed.Count, _files.Count);
        Errors = string.Join(Environment.NewLine, failed);
    }

    [RelayCommand]
    private void Close() => CloseRequested?.Invoke();
}
