namespace RoyConvert.Core.Tests;

public sealed class BatchRenameTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("royconvert-rename-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string[] Create(params string[] names)
    {
        foreach (var name in names)
        {
            File.WriteAllText(Path.Combine(_dir, name), name);
        }

        return names.Select(n => Path.Combine(_dir, n)).ToArray();
    }

    private string[] Names() => Directory.GetFiles(_dir).Select(Path.GetFileName).Order(StringComparer.Ordinal).ToArray()!;

    [Fact]
    public void Plan_NumbersInFileManagerOrder_PaddedToTheLastNumber_KeepsExtensions()
    {
        var files = Create(Enumerable.Range(1, 12).Select(i => $"IMG_{i}.JPG").Append("video.mp4").ToArray());

        var plan = BatchRename.Plan(files, "Tatil {n}");

        Assert.Equal("IMG_1.JPG", Path.GetFileName(plan[0].Source));
        Assert.Equal("Tatil 01.JPG", Path.GetFileName(plan[0].Target));
        Assert.Equal("Tatil 12.JPG", Path.GetFileName(plan[11].Target));
        Assert.Equal("Tatil 13.mp4", Path.GetFileName(plan[12].Target));
        Assert.All(plan, item => Assert.Equal(RenameProblem.None, item.Problem));
    }

    [Fact]
    public void Plan_NameToken_AndStartNumber()
    {
        var files = Create("a.png", "b.png");

        var plan = BatchRename.Plan(files, "{n} - {name}", start: 9);

        Assert.Equal(["09 - a.png", "10 - b.png"], plan.Select(i => Path.GetFileName(i.Target)));
    }

    [Fact]
    public void Plan_UnchangedName_IsNoChange()
    {
        var plan = BatchRename.Plan(Create("a.png"), "{name}");

        Assert.False(Assert.Single(plan).Changes);
    }

    [Theory]
    [InlineData("", RenameProblem.EmptyName)]
    [InlineData("  ", RenameProblem.EmptyName)]
    [InlineData("foto.", RenameProblem.EmptyName)]
    [InlineData("a/b {n}", RenameProblem.InvalidCharacter)]
    [InlineData("ne? {n}", RenameProblem.InvalidCharacter)]
    public void Plan_BadNames_AreProblems(string pattern, RenameProblem problem)
    {
        var plan = BatchRename.Plan(Create("a.png", "b.png"), pattern);

        Assert.All(plan, item => Assert.Equal(problem, item.Problem));
    }

    [Fact]
    public void Plan_SameNameForTwoFiles_IsDuplicate()
    {
        var plan = BatchRename.Plan(Create("a.png", "b.png"), "foto");

        Assert.All(plan, item => Assert.Equal(RenameProblem.Duplicate, item.Problem));
    }

    [Fact]
    public void Plan_NameOfAnUnselectedFile_Exists_IgnoringCase()
    {
        Create("FOTO 1.png");
        var plan = BatchRename.Plan(Create("a.png"), "foto {n}");

        Assert.Equal(RenameProblem.Exists, Assert.Single(plan).Problem);
    }

    [Fact]
    public void Apply_SwapsNames_AndChangesCase()
    {
        var files = Create("1.txt", "2.txt", "kedi.txt");
        var swap = new List<RenameItem>
        {
            new(files[0], Path.Combine(_dir, "2.txt"), RenameProblem.None),
            new(files[1], Path.Combine(_dir, "1.txt"), RenameProblem.None),
            new(files[2], Path.Combine(_dir, "KEDI.txt"), RenameProblem.None),
        };

        BatchRename.Apply(swap);

        Assert.Equal(["1.txt", "2.txt", "KEDI.txt"], Names());
        Assert.Equal("2.txt", File.ReadAllText(Path.Combine(_dir, "1.txt")));
        Assert.Equal("1.txt", File.ReadAllText(Path.Combine(_dir, "2.txt")));
    }

    [Fact]
    public void Apply_WhenAFileIsInUse_UndoesEverything()
    {
        var files = Create("a.txt", "b.txt", "c.txt");
        var plan = BatchRename.Plan(files, "yeni {n}");

        using (File.Open(files[1], FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var error = Assert.Throws<RenameException>(() => BatchRename.Apply(plan));
            Assert.Equal(files[1], error.Path);
            Assert.True(error.InUse);
        }

        Assert.Equal(["a.txt", "b.txt", "c.txt"], Names());
    }

    [Fact]
    public void Apply_RefusesAPlanWithProblems()
    {
        var plan = BatchRename.Plan(Create("a.png", "b.png"), "foto");

        Assert.Throws<ArgumentException>(() => BatchRename.Apply(plan));
        Assert.Equal(["a.png", "b.png"], Names());
    }
}
