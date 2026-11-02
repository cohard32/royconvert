using RoyConvert.App.ViewModels;

namespace RoyConvert.App.Tests;

[Collection(UiCultureCollection.Name)]
public sealed class CombineOrderViewModelTests
{
    private static readonly string Dir = Path.Combine(Path.GetTempPath(), "rc-order");

    private static string F(string name) => Path.Combine(Dir, name);

    private static CombineOrderViewModel Vm() => new([F("sayfa10.pdf"), F("sayfa2.jpg"), F("sayfa1.pdf")]);

    private static string[] Names(CombineOrderViewModel vm) => vm.Items.Select(i => i.Name).ToArray();

    [Fact]
    public void StartsInNaturalNameOrder_FirstSelected()
    {
        var vm = Vm();

        Assert.Equal(["sayfa1.pdf", "sayfa2.jpg", "sayfa10.pdf"], Names(vm));
        Assert.Equal(0, vm.SelectedIndex);
        Assert.False(vm.MoveUpCommand.CanExecute(null));
        Assert.True(vm.MoveDownCommand.CanExecute(null));
        Assert.Contains("3", vm.Title);
    }

    [Fact]
    public void MoveDownAndUp_KeepTheFileSelected()
    {
        var vm = Vm();

        vm.MoveDownCommand.Execute(null);
        vm.MoveDownCommand.Execute(null);
        Assert.Equal(["sayfa2.jpg", "sayfa10.pdf", "sayfa1.pdf"], Names(vm));
        Assert.Equal(2, vm.SelectedIndex);
        Assert.False(vm.MoveDownCommand.CanExecute(null));

        vm.MoveUpCommand.Execute(null);
        Assert.Equal(["sayfa2.jpg", "sayfa1.pdf", "sayfa10.pdf"], Names(vm));
        Assert.Equal(1, vm.SelectedIndex);
    }

    [Fact]
    public void SortByName_PutsTheOrderBack()
    {
        var vm = Vm();
        vm.MoveDownCommand.Execute(null);

        vm.SortByNameCommand.Execute(null);

        Assert.Equal(["sayfa1.pdf", "sayfa2.jpg", "sayfa10.pdf"], Names(vm));
        Assert.Equal(0, vm.SelectedIndex); // still sayfa1.pdf
    }

    [Fact]
    public void Remove_LeavesTheFileOut_NeedsTwoToCombine()
    {
        var vm = Vm();
        IReadOnlyList<string>? confirmed = null;
        vm.Confirmed += files => confirmed = files;

        vm.RemoveCommand.Execute(null);
        Assert.Equal(["sayfa2.jpg", "sayfa10.pdf"], Names(vm));
        Assert.Contains("2", vm.Title);
        Assert.True(vm.CombineCommand.CanExecute(null));

        vm.SelectedIndex = 1;
        vm.RemoveCommand.Execute(null);
        Assert.Equal(0, vm.SelectedIndex);
        Assert.False(vm.CombineCommand.CanExecute(null));

        vm.CombineCommand.Execute(null);
        Assert.Null(confirmed);
    }

    [Fact]
    public void Combine_GivesTheFullPathsInTheChosenOrder()
    {
        var vm = Vm();
        IReadOnlyList<string>? confirmed = null;
        vm.Confirmed += files => confirmed = files;
        vm.SelectedIndex = 2;
        vm.MoveUpCommand.Execute(null);

        vm.CombineCommand.Execute(null);

        Assert.Equal([F("sayfa1.pdf"), F("sayfa10.pdf"), F("sayfa2.jpg")], confirmed);
    }
}
