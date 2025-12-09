using System.Text.Json.Serialization;

public class SortOutputOptions
{
    [JsonPropertyName("includeOriginal")]
    public bool IncludeOriginal { get; set; } = true;

    [JsonPropertyName("includeSorted")]
    public bool IncludeSorted { get; set; } = true;

    [JsonPropertyName("includeOperations")]
    public bool IncludeOperations { get; set; } = true;

    [JsonPropertyName("includeTimestamp")]
    public bool IncludeTimestamp { get; set; } = true;

    [JsonPropertyName("includeDuration")]
    public bool IncludeDuration { get; set; } = true;
}

public class AuthRequest
{
    [JsonPropertyName("login")]
    public string Login { get; set; } = string.Empty;

    [JsonPropertyName("password")]
    public string Password { get; set; } = string.Empty;
}

public class AuthResponse
{
    [JsonPropertyName("token")]
    public string Token { get; set; } = string.Empty;
}

public class ArrayUploadRequest
{
    [JsonPropertyName("numbers")]
    public List<int>? Numbers { get; set; }

    [JsonPropertyName("sourceFilePath")]
    public string? SourceFilePath { get; set; }
}

public class RandomArrayRequest
{
    [JsonPropertyName("length")]
    public int Length { get; set; }

    [JsonPropertyName("minValue")]
    public int MinValue { get; set; }

    [JsonPropertyName("maxValue")]
    public int MaxValue { get; set; }
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ArrayPlacement
{
    Start,
    End,
    AfterIndex
}

public class AddElementsRequest
{
    [JsonPropertyName("placement")]
    public ArrayPlacement Placement { get; set; }

    [JsonPropertyName("afterIndex")]
    public int? AfterIndex { get; set; }

    [JsonPropertyName("values")]
    public List<int>? Values { get; set; }
}

public class SortRequest
{
    [JsonPropertyName("rangeStart")]
    public int? RangeStart { get; set; }

    [JsonPropertyName("rangeEnd")]
    public int? RangeEnd { get; set; }

    [JsonPropertyName("outputOptions")]
    public SortOutputOptions? OutputOptions { get; set; }
}

public class SortResponse
{
    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    [JsonPropertyName("rangeStart")]
    public int? RangeStart { get; set; }

    [JsonPropertyName("rangeEnd")]
    public int? RangeEnd { get; set; }

    [JsonPropertyName("originalNumbers")]
    public int[]? OriginalNumbers { get; set; }

    [JsonPropertyName("sortedNumbers")]
    public int[]? SortedNumbers { get; set; }

    [JsonPropertyName("buildOperations")]
    public int? BuildOperations { get; set; }

    [JsonPropertyName("restoreOperations")]
    public int? RestoreOperations { get; set; }

    [JsonPropertyName("durationMilliseconds")]
    public double? DurationMilliseconds { get; set; }

    [JsonPropertyName("timestampUtc")]
    public DateTimeOffset? TimestampUtc { get; set; }

    [JsonPropertyName("sourceFilePath")]
    public string? SourceFilePath { get; set; }
}

public readonly struct StoredArray
{
    public StoredArray(int[] numbers, string? sourceFilePath, DateTimeOffset updatedAt)
    {
        Numbers = numbers;
        SourceFilePath = sourceFilePath;
        UpdatedAt = updatedAt;
    }

    [JsonIgnore]
    public int[] Numbers { get; }

    [JsonPropertyName("sourceFilePath")]
    public string? SourceFilePath { get; }

    [JsonPropertyName("updatedAt")]
    public DateTimeOffset UpdatedAt { get; }
}

