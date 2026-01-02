using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace AspNetMcpServer;

class InMemoryAuthStore
{
    private readonly ConcurrentDictionary<string, OAuthClient> _clients = new();
    private readonly ConcurrentDictionary<string, AuthorizationCode> _codes = new();
    private readonly ConcurrentDictionary<string, AccessToken> _accessTokens = new();
    private readonly ConcurrentDictionary<string, RefreshToken> _refreshTokens = new();

    public OAuthClient RegisterClient(RegisterRequest request)
    {
        var clientId = CreateToken();
        var clientSecret = CreateToken();
        var redirectUri = string.IsNullOrWhiteSpace(request.RedirectUri) ? "http://localhost:3000/callback" : request.RedirectUri!;

        var client = new OAuthClient(clientId, clientSecret, redirectUri);
        _clients[clientId] = client;
        return client;
    }

    public bool TryGetClient(string clientId, out OAuthClient client) => _clients.TryGetValue(clientId, out client!);

    public AuthorizationCode IssueAuthorizationCode(string clientId, string redirectUri, string scope, string userId)
    {
        var code = new AuthorizationCode(
            CreateToken(16),
            clientId,
            redirectUri,
            string.IsNullOrWhiteSpace(scope) ? "mcp" : scope,
            string.IsNullOrWhiteSpace(userId) ? "demo-user" : userId,
            DateTimeOffset.UtcNow.AddMinutes(5));

        _codes[code.Code] = code;
        return code;
    }

    public bool TryRedeemAuthorizationCode(string codeValue, string redirectUri, string clientId, out AuthorizationCode code)
    {
        code = default!;

        if (!_codes.TryRemove(codeValue, out var stored))
        {
            return false;
        }

        if (stored.ExpiresAt < DateTimeOffset.UtcNow)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(redirectUri) && !string.Equals(redirectUri, stored.RedirectUri, StringComparison.Ordinal))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(clientId) && !string.Equals(clientId, stored.ClientId, StringComparison.Ordinal))
        {
            return false;
        }

        code = stored;
        return true;
    }

    public AccessToken IssueAccessToken(AuthorizationCode code)
    {
        var token = new AccessToken(
            CreateToken(),
            code.ClientId,
            code.UserId,
            code.Scope,
            DateTimeOffset.UtcNow.AddMinutes(20),
            CreateToken());

        var refresh = new RefreshToken(token.RefreshToken, code.ClientId, code.UserId, code.Scope, DateTimeOffset.UtcNow.AddDays(1));

        _accessTokens[token.Token] = token;
        _refreshTokens[refresh.Token] = refresh;

        return token;
    }

    public bool TryUseRefreshToken(string refreshToken, out AccessToken accessToken)
    {
        accessToken = default!;

        if (!_refreshTokens.TryGetValue(refreshToken, out var stored))
        {
            return false;
        }

        if (stored.ExpiresAt < DateTimeOffset.UtcNow)
        {
            _refreshTokens.TryRemove(refreshToken, out _);
            return false;
        }

        var newToken = new AccessToken(
            CreateToken(),
            stored.ClientId,
            stored.UserId,
            stored.Scope,
            DateTimeOffset.UtcNow.AddMinutes(20),
            refreshToken);

        _accessTokens[newToken.Token] = newToken;
        accessToken = newToken;
        return true;
    }

    public bool TryGetActiveToken(string tokenValue, out AccessToken accessToken)
    {
        accessToken = default!;

        if (!_accessTokens.TryGetValue(tokenValue, out var token))
        {
            return false;
        }

        if (token.ExpiresAt < DateTimeOffset.UtcNow)
        {
            _accessTokens.TryRemove(tokenValue, out _);
            return false;
        }

        accessToken = token;
        return true;
    }

    public void Revoke(string token)
    {
        if (_accessTokens.TryRemove(token, out var access))
        {
            _refreshTokens.TryRemove(access.RefreshToken, out _);
        }

        _refreshTokens.TryRemove(token, out _);
        _codes.TryRemove(token, out _);
    }

    private static string CreateToken(int size = 32)
    {
        Span<byte> bytes = stackalloc byte[size];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToHexString(bytes);
    }
}
