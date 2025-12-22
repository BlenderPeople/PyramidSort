using System.Collections.Generic;
using System.Globalization;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);

var serverUrl = Environment.GetEnvironmentVariable("HEAP_SORT_SERVER_URL");
if (!string.IsNullOrWhiteSpace(serverUrl))
{
    builder.WebHost.UseUrls(serverUrl);
}

var paths = EnsureEnvironmentPaths(AppContext.BaseDirectory);
var db = CreateDbManager(paths.DatabasePath);
var tokens = new AuthTokenService();
var store = new UserArrayStore();
var sorter = new HeapSortService();
var app = builder.Build();

app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async context =>
    {
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(new
        {
            message = "Произошла ошибка. Повторите запрос позже."
        });
    });
});

app.MapGet("/", () => Results.Ok("Сервер пирамидальной сортировки готов к работе."));

app.MapPost("/auth/register", ([FromBody] AuthRequest request) =>
    RegisterUser(request, db, tokens));

app.MapPost("/auth/login", ([FromBody] AuthRequest request) =>
    LoginUser(request, db, tokens));

app.MapPost("/auth/logout", (HttpContext context) =>
    LogoutUser(context, tokens));

app.MapPost("/array/upload", ([FromBody] ArrayUploadRequest request, HttpContext context) =>
    UploadArray(request, context, tokens, store, db));

app.MapPost("/array/generate", ([FromBody] RandomArrayRequest request, HttpContext context) =>
    GenerateArray(request, context, tokens, store));

app.MapPost("/array/add", ([FromBody] AddElementsRequest request, HttpContext context) =>
    AppendElements(request, context, tokens, store));

app.MapGet("/array", (HttpContext context) =>
    GetStoredArray(context, tokens, store));

app.MapDelete("/array", (HttpContext context) =>
    ClearStoredArray(context, tokens, store));

app.MapPost("/sort", ([FromBody] SortRequest request, HttpContext context) =>
    SortCurrentArray(request, context, tokens, store, sorter, db));


app.Run();

static IResult RegisterUser(AuthRequest request, DBManager db, AuthTokenService tokens)
{
    if (request == null) return Err(StatusCodes.Status400BadRequest, "Пустой запрос.");
    if (string.IsNullOrWhiteSpace(request.Login) || string.IsNullOrWhiteSpace(request.Password))
        return Err(StatusCodes.Status400BadRequest, "Нужны логин и пароль.");

    var normalizedLogin = request.Login.Trim();

    if (!db.AddUser(normalizedLogin, request.Password))
    {
        return Err(StatusCodes.Status409Conflict, $"Пользователь {normalizedLogin} уже существует.");
    }

    var token = tokens.GenerateToken(normalizedLogin);
    return Results.Ok(new AuthResponse { Token = token });
}

static IResult LoginUser(AuthRequest request, DBManager db, AuthTokenService tokens)
{
    if (request == null) return Err(StatusCodes.Status400BadRequest, "Пустой запрос.");

    if (!db.CheckUser(request.Login, request.Password))
    {
        return Err(StatusCodes.Status401Unauthorized, "Неверные данные.");
    }

    var token = tokens.GenerateToken(request.Login.Trim());
    return Results.Ok(new AuthResponse { Token = token });
}

static IResult LogoutUser(HttpContext context, AuthTokenService tokens)
{
    if (!TryAuthorize(context.Request, tokens, out var login, out var token, out var errorResult))
    {
        return errorResult;
    }

    tokens.RevokeToken(token);
    return Results.Ok(new { message = $"Пользователь {login} успешно вышел из системы." });
}

static IResult UploadArray(ArrayUploadRequest request, HttpContext context, AuthTokenService tokens, UserArrayStore store, DBManager db)
{
    if (!TryAuthorize(context.Request, tokens, out var login, out _, out var errorResult))
    {
        return errorResult;
    }

    if (request == null) return Err(StatusCodes.Status400BadRequest, "Пустой запрос.");

    int[] numbers;
    string? sourcePath = null;

    if (request.Numbers != null && request.Numbers.Count > 0)
    {
        numbers = request.Numbers.ToArray();
        sourcePath = request.SourceFilePath;
    }
    else if (!string.IsNullOrWhiteSpace(request.SourceFilePath))
    {
        sourcePath = request.SourceFilePath;
        if (!Path.IsPathRooted(sourcePath)) return Err(StatusCodes.Status400BadRequest, "Путь должен быть абсолютным.");

        if (!File.Exists(sourcePath))
        {
            var error = $"Файл {sourcePath} не найден.";
            return Err(StatusCodes.Status404NotFound, error);
        }

        if (!TryReadNumbersFromFile(sourcePath, out numbers, out var parseError))
        {
            return Err(StatusCodes.Status400BadRequest, parseError ?? "Ошибка чтения файла.");
        }
    }
    else
    {
        return Err(StatusCodes.Status400BadRequest, "Нужен массив или путь к файлу.");
    }

    store.SetArray(login, numbers, sourcePath);

    return Results.Ok(new
    {
        message = $"Массив сохранён для пользователя {login}.",
        length = numbers.Length,
        source = sourcePath
    });
}

static IResult GenerateArray(RandomArrayRequest request, HttpContext context, AuthTokenService tokens, UserArrayStore store)
{
    if (!TryAuthorize(context.Request, tokens, out var login, out _, out var errorResult))
    {
        return errorResult;
    }

    if (request == null) return Err(StatusCodes.Status400BadRequest, "Пустой запрос.");

    if (request.Length <= 0 || request.Length > 100_000) return Err(StatusCodes.Status400BadRequest, "Длина 1..100000.");

    if (request.MinValue > request.MaxValue) return Err(StatusCodes.Status400BadRequest, "Min > Max.");
    
    if (request.MaxValue == int.MaxValue) return Err(StatusCodes.Status400BadRequest, "Max меньше int.Max.");

    var numbers = new int[request.Length];
    
    var maxExclusive = request.MaxValue + 1;

    for (var i = 0; i < numbers.Length; i++)
    {
        numbers[i] = Random.Shared.Next(request.MinValue, maxExclusive);
    }

    store.SetArray(login, numbers, null);

    return Results.Ok(new
    {
        message = $"Случайный массив из {numbers.Length} элементов подготовлен для пользователя {login}.",
        min = request.MinValue,
        max = request.MaxValue
    });
}

static IResult AppendElements(AddElementsRequest request, HttpContext context, AuthTokenService tokens, UserArrayStore store)
{
    if (!TryAuthorize(context.Request, tokens, out var login, out _, out var errorResult))
    {
        return errorResult;
    }

    if (request == null) return Err(StatusCodes.Status400BadRequest, "Пустой запрос.");
    if (request.Values == null || request.Values.Count == 0) return Err(StatusCodes.Status400BadRequest, "Нет элементов.");
    if (!store.TryGetArray(login, out var stored)) return Err(StatusCodes.Status404NotFound, "Массив не найден.");

    if (!store.TryAppendValues(login, request.Placement, request.Values.ToArray(), request.AfterIndex, out var updatedArray, out var error))
    {
        return Err(StatusCodes.Status400BadRequest, error ?? "Ошибка обновления.");
    }

    return Results.Ok(new
    {
        message = "Массив успешно обновлён.",
        length = updatedArray.Numbers.Length,
        updatedAt = updatedArray.UpdatedAt
    });
}

static IResult GetStoredArray(HttpContext context, AuthTokenService tokens, UserArrayStore store)
{
    if (!TryAuthorize(context.Request, tokens, out var login, out _, out var errorResult))
    {
        return errorResult;
    }

    if (!store.TryGetArray(login, out var stored)) return Err(StatusCodes.Status404NotFound, "Массив не найден.");

    return Results.Ok(new
    {
        numbers = stored.Numbers,
        stored.SourceFilePath,
        stored.UpdatedAt
    });
}

static IResult ClearStoredArray(HttpContext context, AuthTokenService tokens, UserArrayStore store)
{
    if (!TryAuthorize(context.Request, tokens, out var login, out _, out var errorResult))
    {
        return errorResult;
    }

    if (!store.ClearArray(login)) return Err(StatusCodes.Status404NotFound, "Массив не найден.");

    return Results.Ok(new { message = $"Массив пользователя {login} удалён." });
}

static IResult SortCurrentArray(SortRequest request, HttpContext context, AuthTokenService tokens, UserArrayStore store, HeapSortService sorter, DBManager db)
{
    if (!TryAuthorize(context.Request, tokens, out var login, out _, out var errorResult))
    {
        return errorResult;
    }

    if (!store.TryGetArray(login, out var stored)) return Err(StatusCodes.Status404NotFound, "Нет массива.");

    var options = request?.OutputOptions ?? new SortOutputOptions();

    HeapSortResult sortResult;
    try
    {
        sortResult = sorter.Sort(stored.Numbers, request?.RangeStart, request?.RangeEnd);
    }
    catch (Exception)
    {
        return Err(StatusCodes.Status400BadRequest, "Ошибка сортировки.");
    }

    store.SetArray(login, sortResult.SortedNumbers, stored.SourceFilePath);

    var response = new SortResponse
    {
        Message = "Сортировка завершена успешно.",
        RangeStart = sortResult.RangeStart,
        RangeEnd = sortResult.RangeEnd,
        SourceFilePath = stored.SourceFilePath
    };

    if (options.IncludeOriginal)
    {
        response.OriginalNumbers = sortResult.OriginalNumbers;
    }

    if (options.IncludeSorted)
    {
        response.SortedNumbers = sortResult.SortedNumbers;
    }

    if (options.IncludeOperations)
    {
        response.BuildOperations = sortResult.BuildOperations;
        response.RestoreOperations = sortResult.RestoreOperations;
    }

    if (options.IncludeDuration)
    {
        response.DurationMilliseconds = sortResult.DurationMilliseconds;
    }

    if (options.IncludeTimestamp)
    {
        response.TimestampUtc = sortResult.FinishedAt;
    }


    return Results.Ok(response);
}

static bool TryAuthorize(HttpRequest request, AuthTokenService tokens, out string login, out string token, out IResult errorResult)
{
    login = string.Empty;
    token = string.Empty;
    errorResult = Err(StatusCodes.Status401Unauthorized, "Нужна авторизация.");

    if (!request.Headers.TryGetValue("Authorization", out var headerValues))
    {
        errorResult = Err(StatusCodes.Status401Unauthorized, "Нет Authorization.");
        return false;
    }

    var headerValue = headerValues.ToString();
    if (!headerValue.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
    {
        errorResult = Err(StatusCodes.Status401Unauthorized, "Ожидается Bearer токен.");
        return false;
    }

    token = headerValue.Substring("Bearer ".Length).Trim();
    if (token.Length == 0)
    {
        errorResult = Err(StatusCodes.Status401Unauthorized, "Токен пуст.");
        return false;
    }

    if (!tokens.TryValidateToken(token, out login))
    {
        errorResult = Err(StatusCodes.Status401Unauthorized, "Токен недействителен.");
        return false;
    }

    errorResult = Results.Ok();
    return true;
}

static bool TryReadNumbersFromFile(string filePath, out int[] numbers, out string? errorMessage)
{
    numbers = Array.Empty<int>();
    errorMessage = null;

    try
    {
        var content = File.ReadAllText(filePath);
        var parts = content
            .Split(new[] { ' ', '\t', '\r', '\n', ';', ',' }, StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length == 0)
        {
            errorMessage = "Файл не содержит чисел.";
            return false;
        }

        var parsed = new List<int>(parts.Length);
        foreach (var part in parts)
        {
            if (!int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            {
                errorMessage = $"Не удалось преобразовать значение \"{part}\" в целое число.";
                return false;
            }

            parsed.Add(value);
        }

        numbers = parsed.ToArray();
        return true;
    }
    catch (Exception ex)
    {
        errorMessage = $"Ошибка чтения файла: {ex.Message}";
        return false;
    }
}

static IResult Err(int statusCode, string message) =>
    Results.Json(new { message }, statusCode: statusCode);

static AppEnvironmentPaths EnsureEnvironmentPaths(string baseDirectory)
{
    var dataDirectory = Path.Combine(baseDirectory, "data");
    Directory.CreateDirectory(dataDirectory);

    var databasePath = Path.Combine(dataDirectory, "heap_sort.db");

    return new AppEnvironmentPaths(databasePath);
}

static DBManager CreateDbManager(string databasePath)
{
    var manager = new DBManager();
    if (manager.Initialize(databasePath))
    {
        return manager;
    }

    throw new InvalidOperationException("Не удалось инициализировать базу данных.");
}

public sealed record AppEnvironmentPaths(string DatabasePath);
