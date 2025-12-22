using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private static readonly string LogPath = Path.Combine(AppContext.BaseDirectory, "client.log");
    private static HttpClient http = null!;
    private static string? token;
    private static string baseUrl = "http://localhost:5000";
    private static string? currentLogin;

    static void Main(string[] args)
    {
        if (args.Length > 0 && !string.IsNullOrWhiteSpace(args[0]))
        {
            baseUrl = args[0];
        }

        http = new HttpClient { BaseAddress = new Uri(baseUrl) };
        Console.OutputEncoding = Encoding.UTF8;
        Console.WriteLine($"Используем сервер: {baseUrl}");

        while (true)
        {
            PrintMenu();
            Console.Write("Выберите пункт меню: ");
            var choice = Console.ReadLine();

            try
            {
                switch (choice)
                {
                    case "1": Register(); break;
                    case "2": Login(); break;
                    case "3": UploadArray(); break;
                    case "4": GenerateArray(); break;
                    case "5": AddElements(); break;
                    case "6": ShowArray(); break;
                    case "7": SortArray(); break;
                    case "8": ShowLogs(); break;
                    case "9": DeleteArray(); break;
                    case "10": Logout(); break;
                    case "0": return;
                    default: Console.WriteLine("Неизвестный пункт."); break;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Ошибка: " + ex.Message);
            }
        }
    }

    private static void PrintMenu()
    {
        Console.WriteLine();
        Console.WriteLine("1. Регистрация");
        Console.WriteLine("2. Вход");
        Console.WriteLine("3. Загрузить массив");
        Console.WriteLine("4. Сгенерировать массив");
        Console.WriteLine("5. Добавить элементы");
        Console.WriteLine("6. Показать массив");
        Console.WriteLine("7. Отсортировать");
        Console.WriteLine("8. Показать логи (локальные)");
        Console.WriteLine("9. Удалить массив");
        Console.WriteLine("10. Выход из аккаунта");
        Console.WriteLine("0. Выход");
    }

    private static void Register()
    {
        var (login, password) = ReadCredentials();
        var payload = new { login, password };
        var resp = Post("/auth/register", payload);
        var body = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        if (resp.IsSuccessStatusCode)
        {
            var auth = JsonSerializer.Deserialize<AuthResponse>(body, JsonOptions);
            token = auth?.Token;
            currentLogin = login;
        }
        Print(resp, "Регистрация");
    }

    private static void Login()
    {
        var (login, password) = ReadCredentials();
        var payload = new { login, password };
        var resp = Post("/auth/login", payload);
        var body = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        if (resp.IsSuccessStatusCode)
        {
            var auth = JsonSerializer.Deserialize<AuthResponse>(body, JsonOptions);
            token = auth?.Token;
            currentLogin = login;
        }
        Print(resp, "Вход");
    }

    private static void UploadArray()
    {
        EnsureAuth();
        Console.Write("Введите числа через пробел или пусто для файла: ");
        var line = Console.ReadLine();
        if (!string.IsNullOrWhiteSpace(line))
        {
            var numbers = ParseNumbers(line);
            var resp = AuthorizedPost("/array/upload", new { numbers });
            var body = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            Print(resp, "Загрузка массива", body);
            return;
        }

        Console.Write("Путь к файлу: ");
        var path = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(path))
        {
            Console.WriteLine("Путь не может быть пустым.");
            return;
        }
        var respFile = AuthorizedPost("/array/upload", new { sourceFilePath = path });
        var bodyFile = respFile.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        Print(respFile, "Загрузка массива", bodyFile);
    }

    private static void GenerateArray()
    {
        EnsureAuth();
        var length = ReadInt("Длина (1-100000): ", 1, 100_000);
        var min = ReadInt("Минимум: ");
        var max = ReadInt("Максимум: ", min, int.MaxValue - 1);
        var resp = AuthorizedPost("/array/generate", new { length, minValue = min, maxValue = max });
        var body = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        Print(resp, "Генерация", body);
    }

    private static void AddElements()
    {
        EnsureAuth();
        Console.Write("Режим (Start/End/AfterIndex): ");
        var placement = Console.ReadLine()?.Trim();
        if (string.IsNullOrWhiteSpace(placement))
        {
            Console.WriteLine("Режим обязателен.");
            return;
        }
        int? afterIndex = null;
        if (placement.Equals("AfterIndex", StringComparison.OrdinalIgnoreCase))
        {
            afterIndex = ReadInt("Индекс (-1 для начала): ", -1);
        }
        Console.Write("Элементы через пробел: ");
        var line = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(line))
        {
            Console.WriteLine("Пустой список.");
            return;
        }
        var values = ParseNumbers(line);
        var resp = AuthorizedPost("/array/add", new { placement, afterIndex, values });
        var body = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        Print(resp, "Добавление элементов", body);
    }

    private static void ShowArray()
    {
        EnsureAuth();
        var resp = AuthorizedGet("/array");
        var body = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        Print(resp, "Текущий массив", body);
    }

    private static void SortArray()
    {
        EnsureAuth();
        Console.Write("Левая граница (Enter — без): ");
        var left = TryParseNullable(Console.ReadLine());
        Console.Write("Правая граница (Enter — без): ");
        var right = TryParseNullable(Console.ReadLine());

        var resp = AuthorizedPost("/sort", new { rangeStart = left, rangeEnd = right });
        var body = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();

        if (resp.IsSuccessStatusCode)
        {
            SaveClientLog(body);
        }

        Print(resp, "Сортировка", body);
    }

    private static void ShowLogs()
    {
        if (!File.Exists(LogPath))
        {
            Console.WriteLine("Логов нет.");
            return;
        }
        var lines = File.ReadAllLines(LogPath);
        Console.WriteLine("Логи сортировок:");
        foreach (var line in lines)
        {
            Console.WriteLine(line);
        }
    }

    private static void DeleteArray()
    {
        EnsureAuth();
        var resp = AuthorizedDelete("/array");
        var body = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        Print(resp, "Удаление массива", body);
    }

    private static void Logout()
    {
        if (string.IsNullOrWhiteSpace(token))
    {
            Console.WriteLine("Вы не авторизованы.");
            return;
        }
        var resp = AuthorizedPost("/auth/logout", new { });
        var body = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        Print(resp, "Выход", body);
        token = null;
        currentLogin = null;
    }

    private static HttpResponseMessage Post(string url, object payload)
    {
        var content = Serialize(payload);
        return http.PostAsync(url, content).GetAwaiter().GetResult();
    }

    private static HttpResponseMessage AuthorizedPost(string url, object payload)
    {
        AddAuthHeader();
        return Post(url, payload);
    }

    private static HttpResponseMessage AuthorizedGet(string url)
    {
        AddAuthHeader();
        return http.GetAsync(url).GetAwaiter().GetResult();
    }

    private static HttpResponseMessage AuthorizedDelete(string url)
    {
        AddAuthHeader();
        return http.DeleteAsync(url).GetAwaiter().GetResult();
    }

    private static void AddAuthHeader()
    {
        EnsureAuth();
        http.DefaultRequestHeaders.Remove("Authorization");
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    private static void EnsureAuth()
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException("Сначала войдите.");
        }
    }

    private static (string login, string password) ReadCredentials()
    {
        Console.Write("Логин: ");
        var login = (Console.ReadLine() ?? string.Empty).Trim();
        Console.Write("Пароль: ");
        var password = Console.ReadLine() ?? string.Empty;
        return (login, password);
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
            Console.WriteLine("Некорректное число.");
        }
    }

    private static int[] ParseNumbers(string line)
    {
        return line.Split(new[] { ' ', '\t', ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(int.Parse)
            .ToArray();
            }

    private static int? TryParseNullable(string? value)
            {
        return int.TryParse(value, out var result) ? result : null;
    }

    private static StringContent Serialize(object payload)
    {
        var json = JsonSerializer.Serialize(payload, JsonOptions);
        return new StringContent(json, Encoding.UTF8, "application/json");
    }

    private static void Print(HttpResponseMessage resp, string action, string? bodyOverride = null)
    {
        var body = bodyOverride ?? resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        var hasToken = !string.IsNullOrWhiteSpace(body) &&
                       body.IndexOf("\"token\"", StringComparison.OrdinalIgnoreCase) >= 0;

        Console.WriteLine($"[{action}] {(int)resp.StatusCode} ({resp.StatusCode})");
        if (!hasToken && !string.IsNullOrWhiteSpace(body))
        {
            Console.WriteLine(body);
            }
        Console.WriteLine();
    }

    private static void SaveClientLog(string responseBody)
    {
        try
        {
            var id = Guid.NewGuid().ToString("N").Substring(0, 6);
            var line = $"{DateTimeOffset.UtcNow:o} | id={id} | user={currentLogin} | {responseBody}";
            File.AppendAllText(LogPath, line + Environment.NewLine, Encoding.UTF8);
        }
        catch
        {
        }
    }

    private record AuthResponse(string Token);
    }
