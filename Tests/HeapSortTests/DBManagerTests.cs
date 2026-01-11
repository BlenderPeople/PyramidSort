using NUnit.Framework;

[TestFixture]
public class DBManagerTests
{
    private string? _dbPath;

    [TearDown]
    public void Cleanup()
    {
        if (_dbPath == null) return;
        try { File.Delete(_dbPath); } catch { }
        try
        {
            var dir = Path.GetDirectoryName(_dbPath);
            if (!string.IsNullOrEmpty(dir)) Directory.Delete(dir, true);
        }
        catch { }
        _dbPath = null;
    }

    [Test]
    public void AddUser_ThenCheckUser_Works()
    {
        var db = new DBManager();
        _dbPath = CreateTempDbPath();

        Assert.That(db.Initialize(_dbPath), Is.True);
        Assert.That(db.AddUser("login", "pass"), Is.True);

        Assert.That(db.CheckUser("login", "pass"), Is.True);
        Assert.That(db.CheckUser("login", "wrong"), Is.False);
    }

    private static string CreateTempDbPath()
    {
        var dir = Path.Combine(Path.GetTempPath(), "HeapSortTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "test.db");
    }
}

