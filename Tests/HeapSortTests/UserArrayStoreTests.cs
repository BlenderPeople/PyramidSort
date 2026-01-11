using NUnit.Framework;

[TestFixture]
public class UserArrayStoreTests
{
    [Test]
    public void SetArray_ThenTryGetArray_ReturnsSameNumbers()
    {
        var store = new UserArrayStore();
        store.SetArray("user", new[] { 1, 2, 3 }, null);

        var ok = store.TryGetArray("user", out var stored);

        Assert.That(ok, Is.True);
        Assert.That(stored.Numbers, Is.EqualTo(new[] { 1, 2, 3 }));
    }

    [Test]
    public void TryAppendValues_End_AppendsValues()
    {
        var store = new UserArrayStore();
        store.SetArray("user", new[] { 2, 3 }, null);

        var ok = store.TryAppendValues("user", ArrayPlacement.End, new[] { 4, 5 }, null, out var updated, out var error);

        Assert.That(ok, Is.True);
        Assert.That(error, Is.Null);
        Assert.That(updated.Numbers, Is.EqualTo(new[] { 2, 3, 4, 5 }));
    }
}

