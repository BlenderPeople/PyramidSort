using System.Collections.Concurrent;

public class UserArrayStore
{
    private readonly ConcurrentDictionary<string, StoredArray> arrays = new();

    public void SetArray(string login, int[] numbers, string? sourceFilePath)
    {
        var copy = numbers.ToArray();
        var stored = new StoredArray(copy, sourceFilePath, DateTimeOffset.UtcNow);
        arrays[login] = stored;
    }

    public bool TryGetArray(string login, out StoredArray storedArray)
    {
        return arrays.TryGetValue(login, out storedArray);
    }

    public bool ClearArray(string login)
    {
        return arrays.TryRemove(login, out _);
    }

    public bool TryAppendValues(string login, ArrayPlacement placement, int[] values, int? afterIndex, out StoredArray updatedArray, out string? error)
    {
        updatedArray = default;
        error = null;

        if (!arrays.TryGetValue(login, out var stored))
        {
            error = "Массив пользователя не найден.";
            return false;
        }

        var list = stored.Numbers.ToList();

        switch (placement)
        {
            case ArrayPlacement.Start:
                list.InsertRange(0, values);
                break;
            case ArrayPlacement.End:
                list.AddRange(values);
                break;
            case ArrayPlacement.AfterIndex:
                if (afterIndex == null)
                {
                    error = "Для добавления после индекса требуется указать позицию.";
                    return false;
                }

                var index = afterIndex.Value;
                if (index < -1 || index >= list.Count)
                {
                    error = "Указан некорректный индекс для вставки.";
                    return false;
                }

                list.InsertRange(index + 1, values);
                break;
            default:
                error = "Неизвестный режим добавления элементов.";
                return false;
        }

        var updatedNumbers = list.ToArray();
        updatedArray = new StoredArray(updatedNumbers, stored.SourceFilePath, DateTimeOffset.UtcNow);
        arrays[login] = updatedArray;
        return true;
    }
}

