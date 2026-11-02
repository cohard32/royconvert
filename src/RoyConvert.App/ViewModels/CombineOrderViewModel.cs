using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RoyConvert.App.Resources;
using RoyConvert.Core;

namespace RoyConvert.App.ViewModels;

/// <param name="Path">Full path; the list shows <see cref="Name"/>.</param>
public sealed record OrderItem(string Path)
{
    public string Name => System.IO.Path.GetFileName(Path);
}

/// <summary>
/// The order of the files before they are combined into one PDF: first in natural name order, then moved by the user.
/// </summary>
public partial class CombineOrderViewModel : ViewModelBase
{
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(MoveUpCommand), nameof(MoveDownCommand), nameof(RemoveCommand))]
    private int _selectedIndex = -1;

    public CombineOrderViewModel(IEnumerable<string> files)
    {
        Items = new ObservableCollection<OrderItem>(files.Order(NaturalComparer.Instance).Select(f => new OrderItem(f)));
        Items.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(Title));
            CombineCommand.NotifyCanExecuteChanged();
        };
        SelectedIndex = Items.Count > 0 ? 0 : -1;
    }

    /// <summary>Raised with the files in the chosen order when "Combine" is pressed.</summary>
    public event Action<IReadOnlyList<string>>? Confirmed;

    /// <summary>Raised on close without combining.</summary>
    public event Action? Cancelled;

    public ObservableCollection<OrderItem> Items { get; }

    public string Title => string.Format(Strings.Order_Title, Items.Count);

    public IReadOnlyList<string> Files => Items.Select(i => i.Path).ToList();

    private bool CanMoveUp() => SelectedIndex > 0;

    private bool CanMoveDown() => SelectedIndex >= 0 && SelectedIndex < Items.Count - 1;

    private bool HasSelection() => SelectedIndex >= 0 && SelectedIndex < Items.Count;

    [RelayCommand(CanExecute = nameof(CanMoveUp))]
    private void MoveUp() => Move(SelectedIndex, SelectedIndex - 1);

    [RelayCommand(CanExecute = nameof(CanMoveDown))]
    private void MoveDown() => Move(SelectedIndex, SelectedIndex + 1);

    /// <summary>Leaves the file out of this PDF (nothing is deleted).</summary>
    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Remove()
    {
        var index = SelectedIndex;
        Items.RemoveAt(index);
        SelectedIndex = Math.Min(index, Items.Count - 1);
    }

    [RelayCommand]
    private void SortByName()
    {
        var selected = HasSelection() ? Items[SelectedIndex] : null;
        var sorted = Items.OrderBy(i => i.Path, NaturalComparer.Instance).ToList();
        Items.Clear();
        sorted.ForEach(Items.Add);
        SelectedIndex = selected is null ? -1 : Items.IndexOf(selected);
    }

    /// <summary>One file is a PDF too, but "combined" from several is what the user asked for.</summary>
    private bool CanCombine() => Items.Count >= 2;

    [RelayCommand(CanExecute = nameof(CanCombine))]
    private void Combine()
    {
        // Execute does not check CanExecute itself (e.g. Enter on the default button right after a removal).
        if (CanCombine())
        {
            Confirmed?.Invoke(Files);
        }
    }

    [RelayCommand]
    private void Close() => Cancelled?.Invoke();

    private void Move(int from, int to)
    {
        Items.Move(from, to);
        SelectedIndex = to;
    }
}
