namespace RoyConvert.Core.Tests;

public sealed class NaturalComparerTests
{
    [Fact]
    public void Numbers_CompareByValue()
    {
        string[] files = ["scan10.jpg", "Scan2.jpg", "scan1.jpg", "scan02b.jpg", "cover.jpg", "scan2a.jpg"];

        var ordered = files.Order(NaturalComparer.Instance);

        Assert.Equal(["cover.jpg", "scan1.jpg", "Scan2.jpg", "scan2a.jpg", "scan02b.jpg", "scan10.jpg"], ordered);
    }

    [Fact]
    public void LongNumbers_DoNotOverflow()
    {
        Assert.True(NaturalComparer.Instance.Compare("img99999999999999999999.png", "img100000000000000000000.png") < 0);
    }

    [Fact]
    public void EqualIgnoringCase_IsStillATotalOrder()
    {
        Assert.NotEqual(0, NaturalComparer.Instance.Compare("A.jpg", "a.jpg"));
        Assert.Equal(0, NaturalComparer.Instance.Compare("a.jpg", "a.jpg"));
    }
}
