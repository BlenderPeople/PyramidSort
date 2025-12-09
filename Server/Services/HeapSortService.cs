using System.Diagnostics;

public class HeapSortService
{
    public HeapSortResult Sort(int[] numbers, int? rangeStart, int? rangeEnd)
    {
        if (numbers == null)
        {
            throw new ArgumentNullException(nameof(numbers));
        }

        if (numbers.Length == 0)
        {
            throw new InvalidOperationException("Массив не содержит элементов для сортировки.");
        }

        var originalCopy = numbers.ToArray();
        var sortedCopy = numbers.ToArray();

        var start = rangeStart ?? 0;
        var end = rangeEnd ?? (sortedCopy.Length - 1);

        if (start < 0 || start >= sortedCopy.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(rangeStart), "Левая граница выходит за пределы массива.");
        }

        if (end < 0 || end >= sortedCopy.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(rangeEnd), "Правая граница выходит за пределы массива.");
        }

        if (start > end)
        {
            throw new ArgumentException("Левая граница должна быть меньше или равна правой.");
        }

        var length = end - start + 1;
        var segment = new int[length];
        Array.Copy(sortedCopy, start, segment, 0, length);

        var stopwatch = Stopwatch.StartNew();
        var stats = HeapSortSegment(segment);
        stopwatch.Stop();

        Array.Copy(segment, 0, sortedCopy, start, length);

        return new HeapSortResult(
            originalCopy,
            sortedCopy,
            start,
            end,
            stats.BuildOperations,
            stats.RestoreOperations,
            stopwatch.Elapsed.TotalMilliseconds,
            DateTimeOffset.UtcNow);
    }

    private HeapSortStatistics HeapSortSegment(int[] segment)
    {
        var stats = new HeapSortStatistics();
        var length = segment.Length;

        for (var i = length / 2 - 1; i >= 0; i--)
        {
            Heapify(segment, length, i, ref stats, true);
        }

        for (var i = length - 1; i > 0; i--)
        {
            (segment[0], segment[i]) = (segment[i], segment[0]);
            Heapify(segment, i, 0, ref stats, false);
        }

        return stats;
    }

    private void Heapify(int[] array, int length, int index, ref HeapSortStatistics stats, bool countAsBuild)
    {
        if (countAsBuild)
        {
            stats.BuildOperations++;
        }
        else
        {
            stats.RestoreOperations++;
        }

        var current = index;
        while (true)
        {
            var left = 2 * current + 1;
            var right = left + 1;
            var largest = current;

            if (left < length && array[left] > array[largest])
            {
                largest = left;
            }

            if (right < length && array[right] > array[largest])
            {
                largest = right;
            }

            if (largest == current)
            {
                break;
            }

            (array[current], array[largest]) = (array[largest], array[current]);
            current = largest;
            stats.RestoreOperations++;
        }
    }

    private struct HeapSortStatistics
    {
        public int BuildOperations;
        public int RestoreOperations;
    }
}

public readonly struct HeapSortResult
{
    public HeapSortResult(
        int[] originalNumbers,
        int[] sortedNumbers,
        int rangeStart,
        int rangeEnd,
        int buildOperations,
        int restoreOperations,
        double durationMilliseconds,
        DateTimeOffset finishedAt)
    {
        OriginalNumbers = originalNumbers;
        SortedNumbers = sortedNumbers;
        RangeStart = rangeStart;
        RangeEnd = rangeEnd;
        BuildOperations = buildOperations;
        RestoreOperations = restoreOperations;
        DurationMilliseconds = durationMilliseconds;
        FinishedAt = finishedAt;
    }

    public int[] OriginalNumbers { get; }
    public int[] SortedNumbers { get; }
    public int RangeStart { get; }
    public int RangeEnd { get; }
    public int BuildOperations { get; }
    public int RestoreOperations { get; }
    public double DurationMilliseconds { get; }
    public DateTimeOffset FinishedAt { get; }
}

