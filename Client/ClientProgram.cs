using System.Text;
using System.Text.Json;
using System.IO;
using System.Text.RegularExpressions;
using System.Security.Cryptography;

public static class ClientProgram
{
    private static HttpClient httpClient = null!;
    private static string? authToken;
    private static string? currentLogin;
    private static string serverBaseAddress = string.Empty;
    private static readonly string ClientLogPath = Path.Combine(AppContext.BaseDirectory, "client.log");

    public static async Task Main(string[] args)
    {
        serverBaseAddress = ResolveServerBaseAddress(args);
        httpClient = CreateHttpClient(serverBaseAddress);

        Console.OutputEncoding = Encoding.UTF8;
        Console.WriteLine($"Используется сервер: {serverBaseAddress}");
        PrintGreeting();

        while (true)
        {
            PrintMenu();
            Console.Write("Выберите пункт меню: ");
            var choice = Console.ReadLine();

            try
            {
                switch (choice)
                {
                    case "1":
                        await RegisterAsync();
                        break;
                    case "2":
                        await LoginAsync();
                        break;
                    case "3":
                        await UploadArrayAsync();
                        break;
                    case "4":
                        await GenerateArrayAsync();
                        break;
                    case "5":
                        await AddElementsAsync();
                        break;
                    case "6":
                        await ShowStoredArrayAsync();
                        break;
                    case "7":
                        await SortArrayAsync();
                        break;
                    case "8":
                        await ShowLogsAsync();
                        break;
                    case "9":
                        await DeleteArrayAsync();
                        break;
                    case "10":
                        await LogoutAsync();
                        break;
                    case "0":
                        Console.WriteLine("Завершение работы клиента.");
                        return;
                    default:
                        Console.WriteLine("Неизвестный пункт меню, повторите ввод.");
                        break;
                }
            }
            catch (InvalidOperationException ex)
            {
                Console.WriteLine("Ошибка: " + ex.Message);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Непредвиденная ошибка: " + ex.Message);
            }
        }
    }

    private static void PrintGreeting()
    {
        Console.WriteLine("Клиент пирамидальной сортировки");
        Console.WriteLine("Доступные действия: регистрация, вход, загрузка и сортировка массивов.");
        Console.WriteLine();
    }

    private static void PrintMenu()
    {
        Console.WriteLine("1. Регистрация");
        Console.WriteLine("2. Вход");
        Console.WriteLine("3. Загрузить массив с клавиатуры или файла");
        Console.WriteLine("4. Сгенерировать случайный массив");
        Console.WriteLine("5. Добавить элементы в массив");
        Console.WriteLine("6. Просмотреть текущий массив");
        Console.WriteLine("7. Отсортировать массив");
        Console.WriteLine("8. Просмотреть логи сортировок");
        Console.WriteLine("9. Удалить сохранённый массив");
        Console.WriteLine("10. Выход из аккаунта");
        Console.WriteLine("0. Завершить программу");
        Console.WriteLine();
    }

    private static HttpClient CreateHttpClient(string baseAddress)
    {
        var client = new HttpClient
        {
            BaseAddress = new Uri(baseAddress, UriKind.Absolute)
        };
        client.Timeout = TimeSpan.FromSeconds(30);
        return client;
    }

    private static async Task RegisterAsync()
    {
        var (login, password) = ReadCredentials();
        var payload = new { login, password };
        var response = await httpClient.PostAsync("/auth/register", Serialize(payload));
        var body = await response.Content.ReadAsStringAsync();
        if (response.IsSuccessStatusCode)
        {
            var auth = JsonSerializer.Deserialize<AuthTokenResponse>(body, SerializerOptions);
            authToken = auth?.Token;
            currentLogin = login.Trim();
        }

        await PrintResponseAsync(response, "Регистрация", body);
    }

    private static async Task LoginAsync()
    {
        var (login, password) = ReadCredentials();
        var payload = new { login, password };
        var response = await httpClient.PostAsync("/auth/login", Serialize(payload));
        var body = await response.Content.ReadAsStringAsync();
        if (response.IsSuccessStatusCode)
        {
            var auth = JsonSerializer.Deserialize<AuthTokenResponse>(body, SerializerOptions);
            authToken = auth?.Token;
            currentLogin = login.Trim();
        }

        await PrintResponseAsync(response, "Вход", body);
    }

    private static async Task UploadArrayAsync()
    {
        Console.Write("Введите числа через пробел или оставьте пустым для загрузки из файла: ");
        var line = Console.ReadLine();

        if (!string.IsNullOrWhiteSpace(line))
        {
            var numbers = ParseNumbers(line);
            var payload = new { numbers };
            var response = await AuthorizedPostAsync("/array/upload", Serialize(payload));
            await PrintResponseAsync(response, "Загрузка массива");
            return;
        }

        Console.Write("Введите абсолютный путь к файлу: ");
        var path = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(path))
        {
            Console.WriteLine("Путь к файлу не может быть пустым.");
            return;
        }

        var filePayload = new { sourceFilePath = path };
        var fileResponse = await AuthorizedPostAsync("/array/upload", Serialize(filePayload));
        await PrintResponseAsync(fileResponse, "Загрузка массива");
    }

    private static async Task GenerateArrayAsync()
    {
        var length = ReadInt("Введите длину массива (1-100000): ", 1, 100_000);
        var min = ReadInt("Введите минимальное значение: ");
        var max = ReadInt("Введите максимальное значение: ", min, int.MaxValue - 1);

        var payload = new { length, minValue = min, maxValue = max };
        var response = await AuthorizedPostAsync("/array/generate", Serialize(payload));
        await PrintResponseAsync(response, "Генерация массива");
    }

    private static async Task AddElementsAsync()
    {
        Console.WriteLine("Способы добавления: Start, End, AfterIndex");
        Console.Write("Введите режим: ");
        var placement = Console.ReadLine()?.Trim();
        if (string.IsNullOrWhiteSpace(placement))
        {
            Console.WriteLine("Режим не может быть пустым.");
            return;
        }

        int? afterIndex = null;
        if (placement.Equals("AfterIndex", StringComparison.OrdinalIgnoreCase))
        {
            afterIndex = ReadInt("Введите индекс, после которого добавить элементы (начинается с 0, -1 означает начало): ", -1);
        }

        Console.Write("Введите элементы через пробел: ");
        var line = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(line))
        {
            Console.WriteLine("Список элементов не может быть пустым.");
            return;
        }

        var values = ParseNumbers(line);
        var payload = new
        {
            placement = NormalizePlacement(placement),
            afterIndex,
            values
        };

        var response = await AuthorizedPostAsync("/array/add", Serialize(payload));
        await PrintResponseAsync(response, "Добавление элементов");
    }

    private static async Task ShowStoredArrayAsync()
    {
        var response = await AuthorizedGetAsync("/array");
        await PrintResponseAsync(response, "Просмотр массива");
    }

    private static async Task SortArrayAsync()
    {
        Console.Write("Введите левую границу (Enter — без ограничения): ");
        var leftRaw = Console.ReadLine();
        Console.Write("Введите правую границу (Enter — без ограничения): ");
        var rightRaw = Console.ReadLine();

        int? left = TryParseNullable(leftRaw);
        int? right = TryParseNullable(rightRaw);

        EnsureCurrentLogin();
        var clientLogId = GetNextClientLogId();

        var options = new
        {
            includeOriginal = true,
            includeSorted = true,
            includeOperations = true,
            includeTimestamp = true,
            includeDuration = true
        };

        var payload = new
        {
            rangeStart = left,
            rangeEnd = right,
            outputOptions = options,
            saveLog = false // не пишем на сервер
        };

        var response = await AuthorizedPostAsync("/sort", Serialize(payload));
        var body = await response.Content.ReadAsStringAsync();

        if (response.IsSuccessStatusCode)
        {
            TrySaveClientSortLog(body, clientLogId);
        }

        await PrintResponseAsync(response, "Сортировка", body, ResponseMode.SortResult, clientLogId);
    }

    private static async Task ShowLogsAsync()
    {
        EnsureCurrentLogin();
        Console.Write("Введите ID операции (client) для просмотра (Enter — показать список): ");
        var idRaw = Console.ReadLine();
        var logs = LoadClientSortLogs();

        if (!string.IsNullOrWhiteSpace(idRaw))
        {
            var match = logs.FirstOrDefault(x => string.Equals(x.Id, idRaw, StringComparison.OrdinalIgnoreCase));
            if (match != null)
            {
                var detail = JsonSerializer.Serialize(match, SerializerOptions);
                await PrintResponseAsync(new HttpResponseMessage(System.Net.HttpStatusCode.OK), $"Лог {match.Id}", detail, ResponseMode.LogDetail);
                return;
            }

            Console.WriteLine($"Лог с id={idRaw} не найден.");
            return;
        }

        var listJson = JsonSerializer.Serialize(logs, SerializerOptions);
        await PrintResponseAsync(new HttpResponseMessage(System.Net.HttpStatusCode.OK), "Список логов (client)", listJson, ResponseMode.LogsList);
    }

    private static async Task LogoutAsync()
    {
        var response = await AuthorizedPostAsync("/auth/logout", Serialize(new { }));
        await PrintResponseAsync(response, "Выход");
        authToken = null;
        currentLogin = null;
    }

    private static async Task DeleteArrayAsync()
    {
        var response = await AuthorizedDeleteAsync("/array");
        await PrintResponseAsync(response, "Удаление массива");
    }

    private static (string login, string password) ReadCredentials()
    {
        Console.Write("Логин: ");
        var login = Console.ReadLine() ?? string.Empty;
        Console.Write("Пароль: ");
        var password = ReadPassword();
        Console.WriteLine();
        return (login.Trim(), password);
    }

    private static int ReadInt(string prompt, int min = int.MinValue, int max = int.MaxValue)
    {
        while (true)
        {
            Console.Write(prompt);
            var input = Console.ReadLine();
            if (int.TryParse(input, out var value) && value >= min && value <= max)
            {
                return value;
            }

            Console.WriteLine("Некорректное значение, попробуйте ещё раз.");
        }
    }

    private static string ReadPassword()
    {
        var builder = new StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
            {
                break;
            }

            if (key.Key == ConsoleKey.Backspace && builder.Length > 0)
            {
                builder.Length--;
                Console.Write("\b \b");
            }
            else if (!char.IsControl(key.KeyChar))
            {
                builder.Append(key.KeyChar);
                Console.Write("*");
            }
        }

        return builder.ToString();
    }

    private static StringContent Serialize(object payload)
    {
        var json = JsonSerializer.Serialize(payload, SerializerOptions);
        return new StringContent(json, Encoding.UTF8, "application/json");
    }

    private static async Task PrintResponseAsync(HttpResponseMessage response, string action, string? bodyOverride = null, ResponseMode mode = ResponseMode.Default, string? clientLogId = null)
    {
        var color = response.IsSuccessStatusCode ? ConsoleColor.Green : ConsoleColor.Red;
        var previous = Console.ForegroundColor;
        Console.ForegroundColor = color;

        var body = bodyOverride ?? await response.Content.ReadAsStringAsync();
        var sanitizedBody = SanitizeBody(body);
        var summary = BuildSummaryLines(sanitizedBody, mode, clientLogId);

        Console.WriteLine($"[{action}] {(int)response.StatusCode} ({response.StatusCode})");
        if (summary.Count > 0)
        {
            foreach (var line in summary)
            {
                Console.WriteLine(" - " + line);
            }
        }
        else if (!string.IsNullOrWhiteSpace(sanitizedBody))
        {
            if (ContainsToken(sanitizedBody))
            {
                Console.WriteLine("Ответ содержит токен, он сохранён и скрыт.");
            }
            else
            {
                Console.WriteLine(sanitizedBody);
            }
        }
        else
        {
            Console.WriteLine("Ответ пуст.");
        }

        Console.ForegroundColor = previous;
        Console.WriteLine();

        LogResponse(action, response, sanitizedBody);
    }

    private static Task<HttpResponseMessage> AuthorizedPostAsync(string url, HttpContent content)
    {
        EnsureAuthorized();
        AddAuthorizationHeader();
        return httpClient.PostAsync(url, content);
    }

    private static Task<HttpResponseMessage> AuthorizedGetAsync(string url)
    {
        EnsureAuthorized();
        AddAuthorizationHeader();
        return httpClient.GetAsync(url);
    }

    private static Task<HttpResponseMessage> AuthorizedDeleteAsync(string url)
    {
        EnsureAuthorized();
        AddAuthorizationHeader();
        return httpClient.DeleteAsync(url);
    }

    private static void EnsureAuthorized()
    {
        if (string.IsNullOrWhiteSpace(authToken))
        {
            throw new InvalidOperationException("Необходимо выполнить вход перед использованием этой функции.");
        }
    }

    private static void AddAuthorizationHeader()
    {
        httpClient.DefaultRequestHeaders.Remove("Authorization");
        httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {authToken}");
    }

    private static List<string> BuildSummaryLines(string body, ResponseMode mode, string? clientLogId)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return new List<string>();
        }

        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            return mode switch
            {
                ResponseMode.SortResult => BuildSortSummary(root, clientLogId),
                ResponseMode.LogsList => BuildLogsListSummary(root),
                ResponseMode.LogDetail => BuildLogDetailSummary(root),
                _ => BuildGenericSummary(root)
            };
        }
        catch (JsonException)
        {
            return new List<string>();
        }
    }

    private static List<string> BuildGenericSummary(JsonElement root)
    {
        var lines = new List<string>();

        if (root.ValueKind == JsonValueKind.Object)
        {
            lines.AddIfNotNull(GetString(root, "message"));

            if (root.TryGetProperty("token", out _))
            {
                lines.Add("Токен получен и сохранён.");
            }

            lines.AddIfNotNull(FormatIfExists(root, "length", v => $"Длина массива: {v}"));

            lines.AddIfNotNull(GetString(root, "source", prefix: "Источник: "));

            lines.AddIfNotNull(FormatIfExists(root, "logId", v => $"ID лога: {v}"));

            if (root.TryGetProperty("rangeStart", out var rangeStart) || root.TryGetProperty("rangeEnd", out var rangeEnd))
            {
                var startText = root.TryGetProperty("rangeStart", out rangeStart) ? rangeStart.ToString() : "—";
                var endText = root.TryGetProperty("rangeEnd", out rangeEnd) ? rangeEnd.ToString() : "—";
                lines.Add($"Диапазон: {startText}..{endText}");
            }

            lines.AddIfNotNull(FormatIfExists(root, "durationMilliseconds", v => $"Длительность: {v} мс"));

            lines.AddIfNotNull(GetString(root, "timestampUtc", prefix: "Время (UTC): "));

            if (root.TryGetProperty("numbers", out var numbers))
            {
                lines.AddIfNotNull(DescribeArray(numbers, "Текущий массив", full: true));
            }

            if (root.TryGetProperty("sortedNumbers", out var sorted))
            {
                lines.AddIfNotNull(DescribeArray(sorted, "Отсортированный массив", full: true));
            }

            if (root.TryGetProperty("originalNumbers", out var original))
            {
                lines.AddIfNotNull(DescribeArray(original, "Исходный массив", full: true));
            }
        }
        else if (root.ValueKind == JsonValueKind.Array)
        {
            var count = root.GetArrayLength();
            lines.Add($"Получено записей: {count}");

            if (count > 0 && root[0].ValueKind == JsonValueKind.Object)
            {
                var first = root[0];
                if (first.TryGetProperty("id", out var firstId))
                {
                    lines.Add($"Первая запись: id={firstId}");
                }

                if (first.TryGetProperty("timestampUtc", out var firstTs) && firstTs.ValueKind == JsonValueKind.String)
                {
                    lines.Add($"Время первой записи (UTC): {firstTs.GetString()}");
                }
            }
        }

        return lines;
    }

    private static List<string> BuildSortSummary(JsonElement root, string? clientLogId)
    {
        var lines = new List<string>();
        if (root.ValueKind != JsonValueKind.Object)
        {
            return lines;
        }

        if (!string.IsNullOrWhiteSpace(clientLogId))
        {
            lines.Add($"ID операции: {clientLogId}");
        }

        if (root.TryGetProperty("originalNumbers", out var original))
        {
            lines.AddIfNotNull(DescribeArray(original, "Исходный массив", full: true));
        }

        if (root.TryGetProperty("sortedNumbers", out var sorted))
        {
            lines.AddIfNotNull(DescribeArray(sorted, "Отсортированный массив", full: true));
        }

        return lines;
    }

    private static List<string> BuildLogsListSummary(JsonElement root)
    {
        var lines = new List<string>();
        if (root.ValueKind != JsonValueKind.Array)
        {
            return lines;
        }

        var count = root.GetArrayLength();
        lines.Add($"Логов: {count}");

        var take = Math.Min(count, 5);
        for (var i = 0; i < take; i++)
        {
            var item = root[i];
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var id = GetString(item, "id") ?? "?";
            var ts = GetString(item, "timestampUtc") ?? "?";
            var duration = GetString(item, "durationMilliseconds") ?? "?";
            var rangeStart = GetString(item, "rangeStart") ?? "—";
            var rangeEnd = GetString(item, "rangeEnd") ?? "—";

            lines.Add($"#{id}: {ts}, длит. {duration} мс, диапазон {rangeStart}..{rangeEnd}");
        }

        if (count > take)
        {
            lines.Add($"Показано первых {take}, введите ID для детали.");
        }

        return lines;
    }

    private static List<string> BuildLogDetailSummary(JsonElement root)
    {
        var lines = new List<string>();
        if (root.ValueKind != JsonValueKind.Object)
        {
            return lines;
        }

        lines.AddIfNotNull(FormatIfExists(root, "id", v => $"ID: {v}"));
        lines.AddIfNotNull(GetString(root, "timestampUtc", prefix: "Время (UTC): "));
        lines.AddIfNotNull(FormatIfExists(root, "durationMilliseconds", v => $"Длительность: {v} мс"));

        if (root.TryGetProperty("buildOperations", out var bops) && root.TryGetProperty("restoreOperations", out var rops))
        {
            lines.Add($"Операции: build={bops}, restore={rops}");
        }

        if (root.TryGetProperty("rangeStart", out var rs) || root.TryGetProperty("rangeEnd", out var re))
        {
            var startText = root.TryGetProperty("rangeStart", out rs) ? rs.ToString() : "—";
            var endText = root.TryGetProperty("rangeEnd", out re) ? re.ToString() : "—";
            lines.Add($"Диапазон: {startText}..{endText}");
        }

        lines.AddIfNotNull(GetString(root, "sourceFilePath", prefix: "Источник: "));

        if (root.TryGetProperty("originalNumbers", out var original))
        {
            lines.AddIfNotNull(DescribeArray(original, "Исходный массив", full: true));
        }

        if (root.TryGetProperty("sortedNumbers", out var sorted))
        {
            lines.AddIfNotNull(DescribeArray(sorted, "Отсортированный массив", full: true));
        }

        return lines;
    }

    private static void LogResponse(string action, HttpResponseMessage response, string body)
    {
        try
        {
            var builder = new StringBuilder();
            builder.AppendLine($"{DateTimeOffset.UtcNow:o} | {action} | {(int)response.StatusCode} {response.StatusCode}");
            builder.AppendLine(body ?? string.Empty);
            builder.AppendLine(new string('-', 60));
            File.AppendAllText(ClientLogPath, builder.ToString(), Encoding.UTF8);
        }
        catch
        {
        }
    }

    private static void TrySaveClientSortLog(string body, string clientLogId)
    {
        try
        {
            EnsureCurrentLogin();
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return;
            }

            var log = new ClientSortLog
            {
                Id = clientLogId,
                RangeStart = root.TryGetProperty("rangeStart", out var rs) ? rs.GetInt32() : null,
                RangeEnd = root.TryGetProperty("rangeEnd", out var re) ? re.GetInt32() : null,
                DurationMilliseconds = root.TryGetProperty("durationMilliseconds", out var dur) ? dur.GetDouble() : null,
                TimestampUtc = root.TryGetProperty("timestampUtc", out var ts) && ts.ValueKind == JsonValueKind.String
                    ? DateTimeOffset.TryParse(ts.GetString(), out var dto) ? dto : null
                    : null,
                OriginalNumbers = root.TryGetProperty("originalNumbers", out var on) && on.ValueKind == JsonValueKind.Array
                    ? on.EnumerateArray().Select(x => x.GetInt32()).ToArray()
                    : null,
                SortedNumbers = root.TryGetProperty("sortedNumbers", out var sn) && sn.ValueKind == JsonValueKind.Array
                    ? sn.EnumerateArray().Select(x => x.GetInt32()).ToArray()
                    : null,
                BuildOperations = root.TryGetProperty("buildOperations", out var bo) ? bo.GetInt32() : null,
                RestoreOperations = root.TryGetProperty("restoreOperations", out var ro) ? ro.GetInt32() : null
            };

            log.Hash = ComputeLogHash(log);

            var logs = LoadClientSortLogs();
            logs.Add(log);
            var json = JsonSerializer.Serialize(logs, SerializerOptions);
            File.WriteAllText(GetClientSortLogPathForCurrentUser(), json, Encoding.UTF8);
        }
        catch
        {
        }
    }

    private static string GetNextClientLogId()
    {
        var logs = LoadClientSortLogs();
        var nextNumber = logs.Count == 0
            ? 1
            : logs
                .Select(x => int.TryParse(x.Id, out var n) ? n : 0)
                .Max() + 1;
        return nextNumber.ToString("D6"); // фиксированная длина, удобнее читать
    }

    private static List<ClientSortLog> LoadClientSortLogs()
    {
        try
        {
            var path = GetClientSortLogPathForCurrentUser();
            if (!File.Exists(path))
            {
                return new List<ClientSortLog>();
            }

            var json = File.ReadAllText(path, Encoding.UTF8);
            var data = JsonSerializer.Deserialize<List<ClientSortLog>>(json, SerializerOptions);
            if (data == null)
            {
                return new List<ClientSortLog>();
            }

            return data.Where(IsLogHashValid).ToList();
        }
        catch
        {
            return new List<ClientSortLog>();
        }
    }

    private static string DescribeArray(JsonElement element, string title, bool full)
    {
        if (element.ValueKind != JsonValueKind.Array)
        {
            return string.Empty;
        }

        var length = element.GetArrayLength();
        var values = new List<string>(full ? length : Math.Min(length, 10));
        var index = 0;
        foreach (var item in element.EnumerateArray())
        {
            if (!full && index >= 10)
            {
                break;
            }

            values.Add(item.GetRawText());
            index++;
        }

        var suffix = !full && length > values.Count ? " ..." : string.Empty;
        return $"{title}: {length} элемент(ов) [{string.Join(", ", values)}]{suffix}";
    }

    private static bool ContainsToken(string body)
    {
        return !string.IsNullOrWhiteSpace(body) && body.Contains("\"token\"", StringComparison.OrdinalIgnoreCase);
    }

    private static string SanitizeBody(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return body;
        }

        const string pattern = "\"token\"\\s*:\\s*\"[^\"]*\"";
        return Regex.Replace(body, pattern, "\"token\":\"***\"");
    }

    private static string? GetString(JsonElement element, string propertyName, string? prefix = null)
    {
        if (element.TryGetProperty(propertyName, out var prop))
        {
            if (prop.ValueKind == JsonValueKind.String)
            {
                var value = prop.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return prefix != null ? $"{prefix}{value}" : value;
                }
            }
            else
            {
                return prefix != null ? $"{prefix}{prop}" : prop.ToString();
            }
        }

        return null;
    }

    private static string? FormatIfExists(JsonElement element, string propertyName, Func<string, string> formatter)
    {
        if (element.TryGetProperty(propertyName, out var prop))
        {
            var raw = prop.ToString();
            if (!string.IsNullOrWhiteSpace(raw))
            {
                return formatter(raw);
            }
        }

        return null;
    }

    private static void AddIfNotNull(this List<string> lines, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            lines.Add(value);
        }
    }

    private enum ResponseMode
    {
        Default,
        SortResult,
        LogsList,
        LogDetail
    }

    private sealed class ClientSortLog
    {
        public string Id { get; set; } = string.Empty;
        public string? Hash { get; set; }
        public int? RangeStart { get; set; }
        public int? RangeEnd { get; set; }
        public double? DurationMilliseconds { get; set; }
        public DateTimeOffset? TimestampUtc { get; set; }
        public int[]? OriginalNumbers { get; set; }
        public int[]? SortedNumbers { get; set; }
        public int? BuildOperations { get; set; }
        public int? RestoreOperations { get; set; }
    }

    private static void EnsureCurrentLogin()
    {
        if (string.IsNullOrWhiteSpace(currentLogin))
        {
            throw new InvalidOperationException("Необходимо выполнить вход, чтобы работать с логами.");
        }
    }

    private static string GetClientSortLogPathForCurrentUser()
    {
        EnsureCurrentLogin();
        var hash = ComputeLoginHash(currentLogin!);
        return Path.Combine(AppContext.BaseDirectory, $"client_sorts_{hash}.json");
    }

    private static string ComputeLoginHash(string login)
    {
        using var sha = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(login);
        var hash = sha.ComputeHash(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant()[..16]; // компактный префикс
    }

    private static string ComputeLogHash(ClientSortLog log)
    {
        using var sha = SHA256.Create();
        var payload = JsonSerializer.Serialize(log, new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = false,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        });
        var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static bool IsLogHashValid(ClientSortLog log)
    {
        if (string.IsNullOrWhiteSpace(log.Hash))
        {
            return false;
        }

        var clone = new ClientSortLog
        {
            Id = log.Id,
            RangeStart = log.RangeStart,
            RangeEnd = log.RangeEnd,
            DurationMilliseconds = log.DurationMilliseconds,
            TimestampUtc = log.TimestampUtc,
            OriginalNumbers = log.OriginalNumbers,
            SortedNumbers = log.SortedNumbers,
            BuildOperations = log.BuildOperations,
            RestoreOperations = log.RestoreOperations
        };

        var expected = ComputeLogHash(clone);
        return string.Equals(expected, log.Hash, StringComparison.OrdinalIgnoreCase);
    }

    private static int[] ParseNumbers(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return Array.Empty<int>();
        }

        var parts = input.Split(new[] { ' ', '\t', ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
        var numbers = new List<int>(parts.Length);
        foreach (var part in parts)
        {
            if (!int.TryParse(part, out var value))
            {
                throw new InvalidOperationException($"Не удалось преобразовать \"{part}\" в число.");
            }
            numbers.Add(value);
        }

        return numbers.ToArray();
    }

    private static int? TryParseNullable(string? raw)
    {
        if (int.TryParse(raw, out var value))
        {
            return value;
        }

        return null;
    }

    private static bool AskYesNo(string prompt)
    {
        while (true)
        {
            Console.Write(prompt);
            var answer = Console.ReadLine();
            if (string.Equals(answer, "y", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(answer, "д", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (string.Equals(answer, "n", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(answer, "н", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            Console.WriteLine("Введите y (да) или n (нет).");
        }
    }

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private static string NormalizePlacement(string placement)
    {
        return placement.Trim().ToLowerInvariant() switch
        {
            "start" => "Start",
            "end" => "End",
            "afterindex" => "AfterIndex",
            _ => placement
        };
    }

    private static string ResolveServerBaseAddress(string[] args)
    {
        if (TryReadServerFromArgs(args, out var addressFromArgs))
        {
            return NormalizeServerAddress(addressFromArgs);
        }

        var envAddress = Environment.GetEnvironmentVariable("HEAP_SORT_SERVER")
                         ?? Environment.GetEnvironmentVariable("HEAP_SORT_SERVER_URL");

        if (!string.IsNullOrWhiteSpace(envAddress))
        {
            return NormalizeServerAddress(envAddress);
        }

        return "http://localhost:5000";
    }

    private static bool TryReadServerFromArgs(IReadOnlyList<string> args, out string address)
    {
        for (var i = 0; i < args.Count; i++)
        {
            var value = args[i];

            if (value.Equals("--server", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 < args.Count)
                {
                    address = args[i + 1];
                    return true;
                }

                break;
            }

            const string prefix = "--server=";
            if (value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                address = value.Substring(prefix.Length);
                return true;
            }
        }

        address = string.Empty;
        return false;
    }

    private static string NormalizeServerAddress(string raw)
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

    private sealed record AuthTokenResponse(string Token);
}

