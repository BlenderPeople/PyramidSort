using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;

public class AuthTokenService
{
    private readonly ConcurrentDictionary<string, AuthTokenInfo> tokens = new();
    private readonly ConcurrentDictionary<string, string> userTokens = new();
    private readonly TimeSpan lifetime = TimeSpan.FromHours(2);
    private readonly JwtSecurityTokenHandler tokenHandler = new();
    private readonly SymmetricSecurityKey signingKey;
    private readonly SigningCredentials signingCredentials;
    private readonly TokenValidationParameters validationParameters;

    public AuthTokenService()
    {
        signingKey = new SymmetricSecurityKey(RandomNumberGenerator.GetBytes(32));
        signingCredentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);
        validationParameters = new TokenValidationParameters
        {
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = signingKey,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1)
        };
    }

    public string GenerateToken(string login)
    {
        var expiresAt = DateTimeOffset.UtcNow.Add(lifetime);
        var claims = new[]
        {
            new Claim(ClaimTypes.Name, login),
            new Claim(JwtRegisteredClaimNames.Sub, login),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = expiresAt.UtcDateTime,
            SigningCredentials = signingCredentials
        };

        var token = tokenHandler.CreateToken(descriptor);
        var tokenString = tokenHandler.WriteToken(token);

        if (userTokens.TryRemove(login, out var previousToken))
        {
            tokens.TryRemove(previousToken, out _);
        }

        tokens[tokenString] = new AuthTokenInfo(login, expiresAt);
        userTokens[login] = tokenString;

        return tokenString;
    }

    public bool TryValidateToken(string token, out string login)
    {
        login = string.Empty;
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        ClaimsPrincipal principal;
        SecurityToken validatedToken;
        try
        {
            principal = tokenHandler.ValidateToken(token, validationParameters, out validatedToken);
        }
        catch (Exception)
        {
            return false;
        }

        var loginClaim = principal.FindFirst(ClaimTypes.Name) ?? principal.FindFirst(JwtRegisteredClaimNames.Sub);
        if (loginClaim == null)
        {
            return false;
        }

        if (!tokens.TryGetValue(token, out var info))
        {
            return false;
        }

        if (DateTimeOffset.UtcNow > info.ExpiresAt)
        {
            tokens.TryRemove(token, out _);
            if (userTokens.TryGetValue(info.Login, out var cachedToken) && cachedToken == token)
            {
                userTokens.TryRemove(info.Login, out _);
            }

            return false;
        }

        login = loginClaim.Value;
        return true;
    }

    public void RevokeToken(string token)
    {
        if (!tokens.TryRemove(token, out var info))
        {
            return;
        }

        if (userTokens.TryGetValue(info.Login, out var currentToken) && currentToken == token)
        {
            userTokens.TryRemove(info.Login, out _);
        }
    }

    private readonly record struct AuthTokenInfo(string Login, DateTimeOffset ExpiresAt);
}

