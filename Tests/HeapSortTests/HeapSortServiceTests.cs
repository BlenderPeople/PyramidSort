using NUnit.Framework;

[TestFixture]
public class HeapSortServiceTests
{
    [Test]
    public void Sort_UnsortedArray_SortsAscending()
    {
        var sorter = new HeapSortService();
        var input = new[] { 3, 1, 4, 2 };

        var result = sorter.Sort(input, null, null);

        Assert.That(result.SortedNumbers, Is.EqualTo(new[] { 1, 2, 3, 4 }));
    }

    [Test]
    public void Sort_Range()
    {
        var sorter = new HeapSortService();
        var input = new[] { 5, 4, 3, 2, 1 };

        var result = sorter.Sort(input, 1, 3);

        Assert.That(result.SortedNumbers, Is.EqualTo(new[] { 5, 2, 3, 4, 1 }));
    }
 // null array - null&[], random array, pryamoy massiv, negativ array, 
    [Test]
    public void Sort_EmptyInvalidOperationException()
    {
        var sorter = new HeapSortService();
        var input = Array.Empty<int>();

        Assert.Throws<InvalidOperationException>(() => sorter.Sort(input, null, null));
    }

    [Test]
    public void Sort_NullArrayArgumentNullException()
    {
        var sorter = new HeapSortService();

        Assert.Throws<ArgumentNullException>(() => sorter.Sort(null!, null, null));
    }

    [Test]
    public void Sort_PrymoiSortedArray_StaysSorted()
    {
        var sorter = new HeapSortService();
        var input = new[] { 1, 2, 3, 4, 5 };

        var result = sorter.Sort(input, null, null);

        Assert.That(result.SortedNumbers, Is.EqualTo(new[] { 1, 2, 3, 4, 5 }));
    }

    [Test]
    public void Sort_NegativeNumbers_SortsAscending()
    {
        var sorter = new HeapSortService();
        var input = new[] { 3, -1, 2, -5 };

        var result = sorter.Sort(input, null, null);

        Assert.That(result.SortedNumbers, Is.EqualTo(new[] { -5, -1, 2, 3 }));
    }
}