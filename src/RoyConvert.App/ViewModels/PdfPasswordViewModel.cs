using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RoyConvert.App.Resources;
using RoyConvert.Core;

namespace RoyConvert.App.ViewModels;

/// <summary>Puts a password on the selected PDFs, or takes it off; each result is a new file next to its PDF.</summary>
public partial class PdfPasswordViewModel : ViewModelBase
{
    private readonly IReadOnlyList<string> _files;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    [NotifyPropertyChangedFor(nameof(Problem))]
    private string _password = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    [NotifyPropertyChangedFor(nameof(Problem))]
    private string _repeat = string.Empty;

    [ObservableProperty]
    private bool _showPassword;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private string _status = string.Empty;

    [ObservableProperty]
    private string _errors = string.Empty;

    /// <param name="protect">True to put a password on, false to take it off.</param>
    public PdfPasswordViewModel(IEnumerable<string> files, bool protect)
    {
        _files = files.Where(f => File.Exists(f) && Formats.IsPdf(f)).ToList();
        Protects = protect;
        Title = string.Format(protect ? Strings.Password_ProtectTitle : Strings.Password_RemoveTitle, _files.Count);
        if (_files.Count == 0)
        {
            Status = Strings.Password_NoFiles;
        }
    }

    /// <summary>Raised when every file is done, or on close.</summary>
    public event Action? CloseRequested;

    public string Title { get; }

    public bool Protects { get; }

    public string Note => Protects ? Strings.Password_ProtectNote : Strings.Password_RemoveNote;

    public string ApplyText => Protects ? Strings.Password_ProtectApply : Strings.Password_RemoveApply;

    /// <summary>Shown under the fields while adding a password and the two do not match yet.</summary>
    public string Problem => Protects && Repeat.Length > 0 && Repeat != Password ? Strings.Password_Mismatch : string.Empty;

    /// <summary>The character the password boxes show instead of each letter; none while "Show" is ticked.</summary>
    public char Mask => ShowPassword ? '\0' : '●';

    partial void OnShowPasswordChanged(bool value) => OnPropertyChanged(nameof(Mask));

    private bool CanApply() => !IsBusy && _files.Count > 0
        && (Protects ? Password.Length > 0 && Password == Repeat : true);

    [RelayCommand(CanExecute = nameof(CanApply))]
    private async Task ApplyAsync()
    {
        IsBusy = true;
        Errors = string.Empty;
        var password = Password;
        var suffix = Protects ? Strings.Password_ProtectSuffix : Strings.Password_RemoveSuffix;
        var failed = new List<string>();
        for (var i = 0; i < _files.Count; i++)
        {
            Status = string.Format(Strings.Watermark_Progress, i + 1, _files.Count);
            var input = _files[i];
            var output = OutputPath.Beside(input, suffix, "pdf");
            try
            {
                await Task.Run(() =>
                {
                    if (Protects)
                    {
                        PdfSecurity.Protect(input, output, password);
                    }
                    else
                    {
                        PdfSecurity.Unprotect(input, output, password);
                    }
                });
            }
            catch (PdfPasswordException e)
            {
                failed.Add($"{Path.GetFileName(input)}: {Describe(e.Problem)}");
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

        Status = string.Format(Strings.Password_Failed, failed.Count, _files.Count);
        Errors = string.Join(Environment.NewLine, failed);
    }

    [RelayCommand]
    private void Close() => CloseRequested?.Invoke();

    private static string Describe(PdfPasswordProblem problem) => problem switch
    {
        PdfPasswordProblem.WrongPassword => Strings.Password_Wrong,
        PdfPasswordProblem.NotProtected => Strings.Password_NotProtected,
        PdfPasswordProblem.AlreadyProtected => Strings.Password_AlreadyProtected,
        _ => problem.ToString(),
    };
}
