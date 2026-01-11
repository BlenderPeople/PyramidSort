using NUnit.Framework;

[TestFixture]
public class HeapSortServiceTests
{
    [Test]
    public void Sort_FullArray_SortsAscending()
    {
        var sorter = new HeapSortService();
        var input = new[] { 3, 1, 2 };

        var result = sorter.Sort(input, null, null);

        Assert.That(result.SortedNumbers, Is.EqualTo(new[] { 1, 2, 3 }));
    }

    [Test]
    public void Sort_RangeOnly_SortsOnlyThatSegment()
    {
        var sorter = new HeapSortService();
        var input = new[] { 5, 4, 3, 2, 1 };

        var result = sorter.Sort(input, 1, 3);

        Assert.That(result.SortedNumbers, Is.EqualTo(new[] { 5, 2, 3, 4, 1 }));
    }
}

