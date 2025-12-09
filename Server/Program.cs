using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);

var serverUrl = Environment.GetEnvironmentVariable("HEAP_SORT_SERVER_URL");
if (!string.IsNullOrWhiteSpace(serverUrl))
{
    var normalizedUrls = serverUrl
        .Split(';', StringSplitOptions.RemoveEmptyEntries)
        .Select(NormalizeServerUrl)
        .ToArray();

    if (normalizedUrls.Length > 0)
    {
        builder.WebHost.UseUrls(normalizedUrls);
    }
}

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var dataDirectory = Path.Combine(AppContext.BaseDirectory, "data");
Directory.CreateDirectory(dataDirectory);

var logsDirectory = Path.Combine(dataDirectory, "logs");
Directory.CreateDirectory(logsDirectory);

var databasePath = Path.Combine(dataDirectory, "heap_sort.db");

builder.Services.AddSingleton(new AppEnvironmentPaths(databasePath, logsDirectory));
builder.Services.AddSingleton<DBManager>(sp =>
{
    var paths = sp.GetRequiredService<AppEnvironmentPaths>();
    var manager = new DBManager();
    if (!manager.Initialize(paths.DatabasePath))
    {
        throw new InvalidOperationException($"Не удалось инициализировать базу данных по пути {paths.DatabasePath}");
    }

    return manager;
});
builder.Services.AddSingleton<AuthTokenService>();
builder.Services.AddSingleton<UserArrayStore>();
builder.Services.AddSingleton<HeapSortService>();

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();

app.MapGet("/", () => Results.Ok("Сервер пирамидальной сортировки готов к работе."));

app.MapPost("/auth/register", ([FromBody] AuthRequest request, DBManager db, AuthTokenService tokens) =>
    RegisterUser(request, db, tokens));

app.MapPost("/auth/login", ([FromBody] AuthRequest request, DBManager db, AuthTokenService tokens) =>
    LoginUser(request, db, tokens));

app.MapPost("/auth/logout", (HttpContext context, AuthTokenService tokens) =>
    LogoutUser(context, tokens));

app.MapPost("/array/upload", ([FromBody] ArrayUploadRequest request, HttpContext context, AuthTokenService tokens, UserArrayStore store, DBManager db, AppEnvironmentPaths paths) =>
    UploadArray(request, context, tokens, store, db, paths));

app.MapPost("/array/generate", ([FromBody] RandomArrayRequest request, HttpContext context, AuthTokenService tokens, UserArrayStore store) =>
    GenerateArray(request, context, tokens, store));

app.MapPost("/array/add", ([FromBody] AddElementsRequest request, HttpContext context, AuthTokenService tokens, UserArrayStore store) =>
    AppendElements(request, context, tokens, store));

app.MapGet("/array", (HttpContext context, AuthTokenService tokens, UserArrayStore store) =>
    GetStoredArray(context, tokens, store));

app.MapDelete("/array", (HttpContext context, AuthTokenService tokens, UserArrayStore store) =>
    ClearStoredArray(context, tokens, store));

app.MapPost("/sort", ([FromBody] SortRequest request, HttpContext context, AuthTokenService tokens, UserArrayStore store, HeapSortService sorter, DBManager db, AppEnvironmentPaths paths) =>
    SortCurrentArray(request, context, tokens, store, sorter, db, paths));


app.Run();

static IResult RegisterUser(AuthRequest request, DBManager db, AuthTokenService tokens)
{
    if (request == null)
    {
        return Results.BadRequest(new { message = "Тело запроса не должно быть пустым." });
    }

    if (string.IsNullOrWhiteSpace(request.Login) || string.IsNullOrWhiteSpace(request.Password))
    {
        return Results.BadRequest(new { message = "Логин и пароль обязательны для регистрации." });
    }

    var normalizedLogin = request.Login.Trim();

    if (!db.AddUser(normalizedLogin, request.Password))
    {
        return Results.Conflict(new { message = $"Пользователь {normalizedLogin} уже существует или данные некорректны." });
    }

    var token = tokens.GenerateToken(normalizedLogin);
    return Results.Ok(new AuthResponse { Token = token });
}

static IResult LoginUser(AuthRequest request, DBManager db, AuthTokenService tokens)
{
    if (request == null)
    {
        return Results.BadRequest(new { message = "Тело запроса не должно быть пустым." });
    }

    if (!db.CheckUser(request.Login, request.Password))
    {
        return Results.Unauthorized();
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

static IResult UploadArray(ArrayUploadRequest request, HttpContext context, AuthTokenService tokens, UserArrayStore store, DBManager db, AppEnvironmentPaths paths)
{
    if (!TryAuthorize(context.Request, tokens, out var login, out _, out var errorResult))
    {
        return errorResult;
    }

    if (request == null)
    {
        return Results.BadRequest(new { message = "Тело запроса не должно быть пустым." });
    }

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
        if (!Path.IsPathRooted(sourcePath))
        {
            return Results.BadRequest(new { message = "Путь к файлу должен быть абсолютным." });
        }

        if (!File.Exists(sourcePath))
        {
            var error = $"Файл {sourcePath} не найден.";
            db.LogErrorToFile(paths.LogsDirectory, error);
            return Results.NotFound(new { message = error });
        }

        if (!TryReadNumbersFromFile(sourcePath, out numbers, out var parseError))
        {
            db.LogErrorToFile(paths.LogsDirectory, $"Не удалось прочитать массив из файла {sourcePath}: {parseError}");
            return Results.BadRequest(new { message = parseError });
        }
    }
    else
    {
        return Results.BadRequest(new { message = "Необходимо передать массив чисел или путь к файлу." });
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

    if (request == null)
    {
        return Results.BadRequest(new { message = "Тело запроса не должно быть пустым." });
    }

    if (request.Length <= 0 || request.Length > 100_000)
    {
        return Results.BadRequest(new { message = "Длина массива должна быть в диапазоне от 1 до 100000." });
    }

    if (request.MinValue > request.MaxValue)
    {
        return Results.BadRequest(new { message = "Минимальное значение не может превышать максимальное." });
    }

    if (request.MaxValue == int.MaxValue)
    {
        return Results.BadRequest(new { message = "Максимальное значение должно быть меньше int.MaxValue." });
    }

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

    if (request == null)
    {
        return Results.BadRequest(new { message = "Тело запроса не должно быть пустым." });
    }

    if (request.Values == null || request.Values.Count == 0)
    {
        return Results.BadRequest(new { message = "Список добавляемых элементов не может быть пустым." });
    }

    if (!store.TryGetArray(login, out var stored))
    {
        return Results.NotFound(new { message = "У пользователя нет сохранённого массива." });
    }

    if (!store.TryAppendValues(login, request.Placement, request.Values.ToArray(), request.AfterIndex, out var updatedArray, out var error))
    {
        return Results.BadRequest(new { message = error });
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

    if (!store.TryGetArray(login, out var stored))
    {
        return Results.NotFound(new { message = "У пользователя нет сохранённого массива." });
    }

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

    if (!store.ClearArray(login))
    {
        return Results.NotFound(new { message = "У пользователя нет сохранённого массива." });
    }

    return Results.Ok(new { message = $"Массив пользователя {login} удалён." });
}

static IResult SortCurrentArray(SortRequest request, HttpContext context, AuthTokenService tokens, UserArrayStore store, HeapSortService sorter, DBManager db, AppEnvironmentPaths paths)
{
    if (!TryAuthorize(context.Request, tokens, out var login, out _, out var errorResult))
    {
        return errorResult;
    }

    if (!store.TryGetArray(login, out var stored))
    {
        return Results.NotFound(new { message = "Перед сортировкой необходимо загрузить массив." });
    }

    var options = request?.OutputOptions ?? new SortOutputOptions();

    HeapSortResult sortResult;
    try
    {
        sortResult = sorter.Sort(stored.Numbers, request?.RangeStart, request?.RangeEnd);
    }
    catch (Exception ex)
    {
        db.LogErrorToFile(paths.LogsDirectory, $"Ошибка сортировки для пользователя {login}: {ex.Message}");
        return Results.BadRequest(new { message = ex.Message });
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
    errorResult = CreateUnauthorized("Необходима авторизация.");

    if (!request.Headers.TryGetValue("Authorization", out var headerValues))
    {
        errorResult = CreateUnauthorized("Отсутствует заголовок Authorization.");
        return false;
    }

    var headerValue = headerValues.ToString();
    if (!headerValue.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
    {
        errorResult = CreateUnauthorized("Неверный формат заголовка Authorization. Ожидается Bearer токен.");
        return false;
    }

    token = headerValue.Substring("Bearer ".Length).Trim();
    if (token.Length == 0)
    {
        errorResult = CreateUnauthorized("Токен авторизации не может быть пустым.");
        return false;
    }

    if (!tokens.TryValidateToken(token, out login))
    {
        errorResult = CreateUnauthorized("Токен авторизации недействителен.");
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

static IResult CreateUnauthorized(string message)
{
    return Results.Json(new { message }, statusCode: StatusCodes.Status401Unauthorized);
}

static string NormalizeServerUrl(string raw)
{
    var trimmed = (raw ?? string.Empty).Trim();

    if (string.IsNullOrEmpty(trimmed))
    {
        return "http://localhost:5000";
    }

    if (int.TryParse(trimmed, out var port))
    {
        return $"http://localhost:{port}";
    }

    if (!trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
        !trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
    {
        trimmed = $"http://{trimmed}";
    }

    return trimmed.TrimEnd('/');
}

public sealed record AppEnvironmentPaths(string DatabasePath, string LogsDirectory);
