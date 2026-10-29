using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RoyConvert.App.Resources;
using RoyConvert.Core;

namespace RoyConvert.App.ViewModels;

/// <param name="Problem">Why the file cannot get <paramref name="NewName"/>; null if it can.</param>
public sealed record RenameRow(string OldName, string NewName, string? Problem);

/// <summary>Shows what <see cref="BatchRename"/> would do with the pattern as it is typed, and does it.</summary>
public partial class RenameViewModel : ViewModelBase
{
    private readonly IReadOnlyList<string> _files;
    private IReadOnlyList<RenameItem> _plan = [];

    [ObservableProperty]
    private string _pattern = Strings.Rename_DefaultPattern;

    [ObservableProperty]
    private decimal? _start = 1;

    [ObservableProperty]
    private string _status = string.Empty;

    public RenameViewModel(IEnumerable<string> files)
    {
        // A file deleted since it was selected would only make the whole rename fail.
        _files = files.Where(File.Exists).ToList();
        Title = string.Format(Strings.Rename_Title, _files.Count);
        if (_files.Count == 0)
        {
            Status = Strings.Rename_NoFiles;
        }

        UpdatePlan();
    }

    /// <summary>Raised after the files were renamed, or on cancel.</summary>
    public event Action? CloseRequested;

    public string Title { get; }

    public ObservableCollection<RenameRow> Rows { get; } = [];

    partial void OnPatternChanged(string value) => UpdatePlan();

    partial void OnStartChanged(decimal? value) => UpdatePlan();

    private void UpdatePlan()
    {
        _plan = BatchRename.Plan(_files, Pattern, (int)(Start ?? 1));
        Rows.Clear();
        foreach (var item in _plan)
        {
            Rows.Add(new RenameRow(Path.GetFileName(item.Source), Path.GetFileName(item.Target), Describe(item.Problem)));
        }

        ApplyCommand.NotifyCanExecuteChanged();
    }

    private bool CanApply() => _plan.Any(item => item.Changes) && _plan.All(item => item.Problem == RenameProblem.None);

    [RelayCommand(CanExecute = nameof(CanApply))]
    private void Apply()
    {
        try
        {
            BatchRename.Apply(_plan);
            CloseRequested?.Invoke();
        }
        catch (RenameException e)
        {
            var name = Path.GetFileName(e.Path);
            Status = string.Format(Strings.Rename_Failed, e.InUse ? string.Format(Strings.Rename_InUse, name) : $"{name}: {e.Message}");
        }
    }

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke();

    private static string? Describe(RenameProblem problem) => problem switch
    {
        RenameProblem.None => null,
        RenameProblem.EmptyName => Strings.Rename_EmptyName,
        RenameProblem.InvalidCharacter => Strings.Rename_InvalidCharacter,
        RenameProblem.Duplicate => Strings.Rename_Duplicate,
        RenameProblem.Exists => Strings.Rename_Exists,
        _ => problem.ToString(),
    };
}
