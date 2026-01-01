using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;

var builder = WebApplication.CreateBuilder(args);

var serverOptions = ServerOptions.FromConfiguration(builder.Configuration);

builder.WebHost.UseUrls($"http://0.0.0.0:{serverOptions.Port}");

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy => policy
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowAnyOrigin());
});
builder.Services.AddSingleton(serverOptions);
builder.Services.AddSingleton<InMemoryAuthStore>();

var app = builder.Build();
app.UseCors();

var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
{
    PropertyNameCaseInsensitive = true
};

app.MapGet("/", (ServerOptions options) =>
{
    return Results.Content(SplashPage(options), "text/html");
});

app.MapGet("/.well-known/oauth-authorization-server", (ServerOptions options) =>
{
    var authServerUrl = options.AuthServerUrl;

    return Results.Json(new
    {
        issuer = authServerUrl,
        authorization_endpoint = $"{authServerUrl}/authorize",
        token_endpoint = $"{authServerUrl}/token",
        registration_endpoint = $"{authServerUrl}/register",
        introspection_endpoint = $"{authServerUrl}/introspect",
        revocation_endpoint = $"{authServerUrl}/revoke",
        token_endpoint_auth_methods_supported = new[] { "none" },
        response_types_supported = new[] { "code" },
        grant_types_supported = new[] { "authorization_code", "refresh_token" },
        code_challenge_methods_supported = new[] { "S256" },
        service_documentation = "https://modelcontextprotocol.io"
    }, options: jsonOptions);
});

app.MapPost("/register", (RegisterRequest? request, InMemoryAuthStore store) =>
{
    var registration = store.RegisterClient(request ?? new RegisterRequest(null, null));

    return Results.Json(new
    {
        client_id = registration.Id,
        client_secret = registration.Secret,
        redirect_uris = new[] { registration.RedirectUri },
        token_endpoint_auth_method = "none"
    }, options: jsonOptions);
});

app.MapGet("/authorize", (HttpContext context, InMemoryAuthStore store) =>
{
    var query = context.Request.Query;
    var clientId = query["client_id"].ToString();
    var state = query["state"].ToString();
    var scope = query["scope"].ToString();
    var user = query["user"].ToString();
    var redirectUri = query["redirect_uri"].ToString();

    if (string.IsNullOrWhiteSpace(clientId))
    {
        return Results.BadRequest(new { error = "client_id is required" });
    }

    if (!store.TryGetClient(clientId, out var client))
    {
        return Results.BadRequest(new { error = "unknown client_id" });
    }

    if (string.IsNullOrWhiteSpace(redirectUri))
    {
        redirectUri = client.RedirectUri;
    }

    var code = store.IssueAuthorizationCode(clientId, redirectUri, scope, user);

    var redirectUrl = QueryHelpers.AddQueryString(redirectUri, new Dictionary<string, string?>
    {
        ["code"] = code.Code,
        ["state"] = state
    });

    return Results.Redirect(redirectUrl);
});

app.MapPost("/token", async (HttpContext context, InMemoryAuthStore store) =>
{
    var form = await context.Request.ReadFormAsync();
    var grantType = form["grant_type"].ToString();

    if (grantType == "authorization_code")
    {
        var codeValue = form["code"].ToString();
        var redirectUri = form["redirect_uri"].ToString();
        var clientId = form["client_id"].ToString();

        if (!store.TryRedeemAuthorizationCode(codeValue, redirectUri, clientId, out var code))
        {
            return Results.BadRequest(new { error = "invalid_grant" });
        }

        var token = store.IssueAccessToken(code);

        return Results.Json(token.ToResponse(), options: jsonOptions);
    }

    if (grantType == "refresh_token")
    {
        var refresh = form["refresh_token"].ToString();
        if (!store.TryUseRefreshToken(refresh, out var refreshed))
        {
            return Results.BadRequest(new { error = "invalid_grant" });
        }

        return Results.Json(refreshed.ToResponse(), options: jsonOptions);
    }

    return Results.BadRequest(new { error = "unsupported_grant_type" });
});

app.MapPost("/introspect", async (HttpContext context, InMemoryAuthStore store) =>
{
    var form = await context.Request.ReadFormAsync();
    var tokenValue = form["token"].ToString();

    if (store.TryGetActiveToken(tokenValue, out var token))
    {
        return Results.Json(new
        {
            active = true,
            scope = token.Scope,
            client_id = token.ClientId,
            sub = token.UserId,
            exp = token.ExpiresAt.ToUnixTimeSeconds()
        }, options: jsonOptions);
    }

    return Results.Json(new { active = false }, options: jsonOptions);
});

app.MapPost("/revoke", async (HttpContext context, InMemoryAuthStore store) =>
{
    var form = await context.Request.ReadFormAsync();
    var token = form["token"].ToString();

    if (string.IsNullOrWhiteSpace(token))
    {
        return Results.BadRequest(new { error = "token is required" });
    }

    store.Revoke(token);
    return Results.Json(new { revoked = true }, options: jsonOptions);
});

app.MapMethods("/mcp", new[] { "GET", "POST", "DELETE" }, async context =>
{
    if (!TryAuthenticate(context, context.RequestServices.GetRequiredService<InMemoryAuthStore>(), out var accessToken, out var failure))
    {
        await failure.ExecuteAsync(context);
        return;
    }

    context.Response.ContentType = "application/json";

    if (context.Request.Method == "GET")
    {
        var response = new
        {
            jsonrpc = "2.0",
            id = (string?)null,
            result = new
            {
                message = "ASP.NET Core MCP endpoint ready",
                user = accessToken.UserId,
                scope = accessToken.Scope
            }
        };

        await context.Response.WriteAsJsonAsync(response, jsonOptions);
        return;
    }

    if (context.Request.Method == "DELETE")
    {
        await context.Response.WriteAsJsonAsync(new
        {
            jsonrpc = "2.0",
            id = (string?)null,
            result = new { message = "session closed" }
        }, jsonOptions);
        return;
    }

    var request = await JsonSerializer.DeserializeAsync<McpRequest>(context.Request.Body, jsonOptions);

    if (request?.Method?.Equals("ping", StringComparison.OrdinalIgnoreCase) == true)
    {
        await context.Response.WriteAsJsonAsync(new
        {
            jsonrpc = "2.0",
            id = request.Id,
            result = new { message = "pong" }
        }, jsonOptions);
        return;
    }

    if (request?.Method?.Equals("echo", StringComparison.OrdinalIgnoreCase) == true &&
        request.Params?.ValueKind == JsonValueKind.Object &&
        request.Params.Value.TryGetProperty("text", out var text))
    {
        await context.Response.WriteAsJsonAsync(new
        {
            jsonrpc = "2.0",
            id = request.Id,
            result = new { echoed = text.GetString() }
        }, jsonOptions);
        return;
    }

    await context.Response.WriteAsJsonAsync(new
    {
        jsonrpc = "2.0",
        id = request?.Id,
        error = new
        {
            code = -32601,
            message = "Method not found"
        }
    }, jsonOptions);
});

app.MapGet("/sse", async context =>
{
    if (!TryAuthenticate(context, context.RequestServices.GetRequiredService<InMemoryAuthStore>(), out var token, out var failure))
    {
        await failure.ExecuteAsync(context);
        return;
    }

    context.Response.Headers.CacheControl = "no-store";
    context.Response.Headers.Connection = "keep-alive";
    context.Response.Headers.ContentType = "text/event-stream";

    await context.Response.WriteAsync($"event: message\ndata: {JsonSerializer.Serialize(new { message = "connected", user = token.UserId })}\n\n");
    await context.Response.Body.FlushAsync();

    var heartbeat = new PeriodicTimer(TimeSpan.FromSeconds(15));

    try
    {
        while (await heartbeat.WaitForNextTickAsync(context.RequestAborted))
        {
            await context.Response.WriteAsync("event: ping\ndata: {}\n\n");
            await context.Response.Body.FlushAsync();
        }
    }
    catch (OperationCanceledException)
    {
        // Client disconnected
    }
});

app.MapPost("/message", async context =>
{
    if (!TryAuthenticate(context, context.RequestServices.GetRequiredService<InMemoryAuthStore>(), out _, out var failure))
    {
        await failure.ExecuteAsync(context);
        return;
    }

    var payload = await JsonSerializer.DeserializeAsync<JsonElement>(context.Request.Body, jsonOptions);

    await context.Response.WriteAsJsonAsync(new
    {
        jsonrpc = "2.0",
        id = (string?)null,
        result = new
        {
            status = "accepted",
            payload
        }
    }, jsonOptions);
});

app.Run();

static bool TryAuthenticate(HttpContext context, InMemoryAuthStore store, out AccessToken accessToken, out IResult failure)
{
    accessToken = default!;
    failure = Results.Unauthorized();

    if (!context.Request.Headers.TryGetValue("Authorization", out var authHeader))
    {
        failure = Results.Unauthorized();
        return false;
    }

    var header = authHeader.ToString();
    if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
    {
        failure = Results.Unauthorized();
        return false;
    }

    var tokenValue = header["Bearer ".Length..].Trim();

    if (!store.TryGetActiveToken(tokenValue, out var token))
    {
        failure = Results.Unauthorized();
        return false;
    }

    accessToken = token;
    return true;
}

static string SplashPage(ServerOptions options)
{
    return $@"
<!DOCTYPE html>
<html>
  <head>
    <meta charset=""UTF-8"" />
    <title>MCP ASP.NET Core Server</title>
    <style>
      body {{ font-family: system-ui, sans-serif; max-width: 900px; margin: 40px auto; padding: 16px; }}
      code {{ background: #f6f8fa; padding: 2px 4px; border-radius: 4px; }}
      .endpoint {{ background: #f6f8fa; padding: 8px; margin: 6px 0; border-radius: 6px; font-family: monospace; }}
    </style>
  </head>
  <body>
    <h1>MCP Reference - ASP.NET Core</h1>
    <p>Minimal port of the Node.js MCP reference server running on ASP.NET Core.</p>
    <h2>OAuth Endpoints ({options.AuthMode})</h2>
    <div class=""endpoint"">GET {options.AuthServerUrl}/authorize</div>
    <div class=""endpoint"">POST {options.AuthServerUrl}/token</div>
    <div class=""endpoint"">POST {options.AuthServerUrl}/introspect</div>
    <div class=""endpoint"">POST {options.AuthServerUrl}/revoke</div>
    <div class=""endpoint"">POST {options.AuthServerUrl}/register</div>
    <h2>MCP Endpoints</h2>
    <div class=""endpoint"">GET/POST/DELETE {options.BaseUri}/mcp</div>
    <div class=""endpoint"">GET {options.BaseUri}/sse</div>
    <div class=""endpoint"">POST {options.BaseUri}/message</div>
  </body>
</html>";
}

record RegisterRequest(string? ClientName, string? RedirectUri);

record McpRequest
{
    public string? Jsonrpc { get; init; }
    public object? Id { get; init; }
    public string? Method { get; init; }
    public JsonElement? Params { get; init; }
}

record AuthorizationCode(string Code, string ClientId, string RedirectUri, string Scope, string UserId, DateTimeOffset ExpiresAt);

record OAuthClient(string Id, string Secret, string RedirectUri);

record AccessToken(string Token, string ClientId, string UserId, string Scope, DateTimeOffset ExpiresAt, string RefreshToken)
{
    public object ToResponse() => new
    {
        access_token = Token,
        token_type = "bearer",
        expires_in = (int)Math.Max(0, (ExpiresAt - DateTimeOffset.UtcNow).TotalSeconds),
        refresh_token = RefreshToken,
        scope = Scope
    };
}

record RefreshToken(string Token, string ClientId, string UserId, string Scope, DateTimeOffset ExpiresAt);

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

record ServerOptions(string BaseUri, int Port, string AuthMode, string? ExternalAuthServerUrl)
{
    public string AuthServerUrl => AuthMode.Equals("external", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(ExternalAuthServerUrl)
        ? ExternalAuthServerUrl!
        : BaseUri;

    public static ServerOptions FromConfiguration(IConfiguration configuration)
    {
        var port = configuration.GetValue<int?>("PORT")
                   ?? configuration.GetSection("Server").GetValue<int?>("Port")
                   ?? 3232;

        var baseUri = configuration["BASE_URI"]
                      ?? configuration.GetSection("Server").GetValue<string?>("BaseUri")
                      ?? $"http://localhost:{port}";

        var authMode = configuration["AUTH_MODE"]
                       ?? configuration.GetSection("Server").GetValue<string?>("AuthMode")
                       ?? "internal";

        var externalUrl = configuration["AUTH_SERVER_URL"]
                          ?? configuration.GetSection("Server").GetValue<string?>("ExternalAuthServerUrl");

        return new ServerOptions(NormalizeBaseUri(baseUri), port, authMode, NormalizeUrl(externalUrl));
    }

    private static string NormalizeBaseUri(string uri) => NormalizeUrl(uri) ?? "http://localhost:3232";

    private static string? NormalizeUrl(string? uri)
    {
        if (string.IsNullOrWhiteSpace(uri))
        {
            return null;
        }

        return uri.EndsWith("/")
            ? uri.TrimEnd('/')
            : uri;
    }
}
