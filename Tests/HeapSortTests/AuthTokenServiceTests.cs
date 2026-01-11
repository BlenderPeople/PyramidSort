using NUnit.Framework;

[TestFixture]
public class AuthTokenServiceTests
{
    [Test]
    public void GenerateToken_ThenValidate_Succeeds()
    {
        var tokens = new AuthTokenService();

        var token = tokens.GenerateToken("user");
        var ok = tokens.TryValidateToken(token, out var login);

        Assert.That(ok, Is.True);
        Assert.That(login, Is.EqualTo("user"));
    }

    [Test]
    public void RevokeToken_MakesTokenInvalid()
    {
        var tokens = new AuthTokenService();

        var token = tokens.GenerateToken("user");
        tokens.RevokeToken(token);

        Assert.That(tokens.TryValidateToken(token, out _), Is.False);
    }
}

