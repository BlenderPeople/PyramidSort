using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;

public class DBManager
{
    private SqliteConnection? connection;
    private readonly object syncRoot = new();
    private string? databasePath;

    public bool Initialize(string dbPath)
    {
        if (string.IsNullOrWhiteSpace(dbPath))
        {
            return false;
        }

        try
        {
            var directory = Path.GetDirectoryName(dbPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("Не удалось создать каталог базы данных: " + ex.Message);
            return false;
        }

        databasePath = dbPath;

        if (!Connect())
        {
            return false;
        }

        return EnsureSchema();
    }

    public bool AddUser(string login, string password)
    {
        if (!Connect())
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(login) || string.IsNullOrWhiteSpace(password))
        {
            return false;
        }

        var normalizedLogin = login.Trim();

        try
        {
            using var checkCommand = connection!.CreateCommand();
            checkCommand.CommandText = "SELECT COUNT(1) FROM users WHERE Login = $login";
            checkCommand.Parameters.AddWithValue("$login", normalizedLogin);
            var exists = Convert.ToInt32(checkCommand.ExecuteScalar());
            if (exists > 0)
            {
                return false;
            }

            var passwordData = CreatePasswordHash(password);

            using var insertCommand = connection.CreateCommand();
            insertCommand.CommandText = @"INSERT INTO users (Login, PasswordHash, PasswordSalt)
                                          VALUES ($login, $hash, $salt)";
            insertCommand.Parameters.AddWithValue("$login", normalizedLogin);
            insertCommand.Parameters.AddWithValue("$hash", passwordData.hash);
            insertCommand.Parameters.AddWithValue("$salt", passwordData.salt);

            return insertCommand.ExecuteNonQuery() == 1;
        }
        catch (Exception ex)
        {
            Console.WriteLine("Ошибка добавления пользователя: " + ex.Message);
            return false;
        }
    }

    public bool CheckUser(string login, string password)
    {
        if (!Connect())
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(login) || string.IsNullOrWhiteSpace(password))
        {
            return false;
        }

        try
        {
            using var command = connection!.CreateCommand();
            command.CommandText = "SELECT PasswordHash, PasswordSalt FROM users WHERE Login = $login LIMIT 1";
            command.Parameters.AddWithValue("$login", login.Trim());

            using var reader = command.ExecuteReader();
            if (!reader.Read())
            {
                return false;
            }

            var storedHash = reader.GetString(0);
            var storedSalt = reader.GetString(1);

            return VerifyPassword(password, storedHash, storedSalt);
        }
        catch (Exception ex)
        {
            Console.WriteLine("Ошибка проверки пользователя: " + ex.Message);
            return false;
        }
    }

    public void LogErrorToFile(string logsDirectory, string message)
    {
        try
        {
            Directory.CreateDirectory(logsDirectory);
            var filePath = Path.Combine(logsDirectory, "errors.log");
            var fullMessage = $"{DateTimeOffset.UtcNow:o} | {message}{Environment.NewLine}";
            File.AppendAllText(filePath, fullMessage, Encoding.UTF8);
        }
        catch (Exception ex)
        {
            Console.WriteLine("Не удалось записать ошибку в файл: " + ex.Message);
        }
    }

    private bool Connect()
    {
        lock (syncRoot)
        {
            if (connection != null && connection.State == System.Data.ConnectionState.Open)
            {
                return true;
            }

            if (string.IsNullOrWhiteSpace(databasePath))
            {
                return false;
            }

            try
            {
                connection = new SqliteConnection("Data Source=" + databasePath);
                connection.Open();
                return connection.State == System.Data.ConnectionState.Open;
            }
            catch (Exception ex)
            {
                Console.WriteLine("Ошибка подключения к базе данных: " + ex.Message);
                return false;
            }
        }
    }

    private bool EnsureSchema()
    {
        try
        {
            using var users = connection!.CreateCommand();
            users.CommandText = @"CREATE TABLE IF NOT EXISTS users (
                                      Id INTEGER PRIMARY KEY AUTOINCREMENT,
                                      Login TEXT NOT NULL UNIQUE,
                                      PasswordHash TEXT NOT NULL,
                                      PasswordSalt TEXT NOT NULL
                                  );";
            users.ExecuteNonQuery();

            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine("Ошибка создания схемы базы данных: " + ex.Message);
            return false;
        }
    }

    private static (string hash, string salt) CreatePasswordHash(string password)
    {
        var saltBytes = RandomNumberGenerator.GetBytes(16);
        using var pbkdf2 = new Rfc2898DeriveBytes(password, saltBytes, 100_000, HashAlgorithmName.SHA256);
        var hashBytes = pbkdf2.GetBytes(32);
        return (Convert.ToBase64String(hashBytes), Convert.ToBase64String(saltBytes));
    }

    private static bool VerifyPassword(string password, string storedHash, string storedSalt)
    {
        try
        {
            var saltBytes = Convert.FromBase64String(storedSalt);
            using var pbkdf2 = new Rfc2898DeriveBytes(password, saltBytes, 100_000, HashAlgorithmName.SHA256);
            var computedBytes = pbkdf2.GetBytes(32);
            var storedHashBytes = Convert.FromBase64String(storedHash);
            return CryptographicOperations.FixedTimeEquals(storedHashBytes, computedBytes);
        }
        catch
        {
            return false;
        }
    }
}

