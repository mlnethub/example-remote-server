using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text;
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

var tools = ToolDefinitions();
var resources = ResourceDefinitions();

const string McpTinyImage =
  "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";
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

    if (request?.Method == "tools/list")
    {
        await context.Response.WriteAsJsonAsync(new
        {
            jsonrpc = "2.0",
            id = request.Id,
            result = new { tools }
        }, jsonOptions);
        return;
    }

    if (request?.Method == "tools/call")
    {
        var name = request.Params?.GetPropertyOrDefault("name")?.GetString();
        var arguments = request.Params?.GetPropertyOrDefault("arguments");

        var response = name switch
        {
            ToolNames.Echo => CallEcho(arguments),
            ToolNames.Add => CallAdd(arguments),
            ToolNames.LongRunningOperation => await CallLongRunning(arguments),
            ToolNames.SampleLLM => CallSampleLLM(arguments),
            ToolNames.GetTinyImage => CallGetTinyImage(),
            ToolNames.AnnotatedMessage => CallAnnotatedMessage(arguments),
            ToolNames.GetResourceReference => CallResourceReference(arguments, resources),
            ToolNames.ElicitInputs => CallElicitInputs(),
            ToolNames.McpAppsHelloWorld => CallMcpAppsHelloWorld(),
            _ => null
        };

        if (response is not null)
        {
            await context.Response.WriteAsJsonAsync(new
            {
                jsonrpc = "2.0",
                id = request.Id,
                result = response
            }, jsonOptions);
            return;
        }
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
        return false;
    }

    var header = authHeader.ToString();
    if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
    {
        return false;
    }

    var tokenValue = header["Bearer ".Length..].Trim();

    if (!store.TryGetActiveToken(tokenValue, out var token))
    {
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

static JsonElement Schema(object value) => JsonSerializer.SerializeToElement(value);

static IReadOnlyList<ToolDefinition> ToolDefinitions()
{
    return new List<ToolDefinition>
    {
        new(ToolNames.Echo, "Echoes back the input", Schema(new
        {
            type = "object",
            properties = new
            {
                message = new { type = "string", description = "Message to echo" }
            },
            required = new[] { "message" }
        })),
        new(ToolNames.Add, "Adds two numbers", Schema(new
        {
            type = "object",
            properties = new
            {
                a = new { type = "number", description = "First number" },
                b = new { type = "number", description = "Second number" }
            },
            required = new[] { "a", "b" }
        })),
        new(ToolNames.LongRunningOperation, "Demonstrates a long running operation with progress updates", Schema(new
        {
            type = "object",
            properties = new
            {
                duration = new { type = "number", description = "Duration in seconds", @default = 10 },
                steps = new { type = "number", description = "Number of steps", @default = 5 }
            }
        })),
        new(ToolNames.SampleLLM, "Samples from an LLM (stubbed response)", Schema(new
        {
            type = "object",
            properties = new
            {
                prompt = new { type = "string", description = "Prompt to send" },
                maxTokens = new { type = "number", description = "Maximum tokens", @default = 100 }
            },
            required = new[] { "prompt" }
        })),
        new(ToolNames.GetTinyImage, "Returns the MCP tiny image", Schema(new { type = "object", properties = new { } })),
        new(ToolNames.AnnotatedMessage, "Demonstrates annotations on messages", Schema(new
        {
            type = "object",
            properties = new
            {
                messageType = new { type = "string", @enum = new[] { "error", "success", "debug" } },
                includeImage = new { type = "boolean", @default = false }
            },
            required = new[] { "messageType" }
        })),
        new(ToolNames.GetResourceReference, "Returns a resource reference", Schema(new
        {
            type = "object",
            properties = new
            {
                resourceId = new { type = "integer", minimum = 1, maximum = 100 }
            },
            required = new[] { "resourceId" }
        })),
        new(ToolNames.ElicitInputs, "Demonstrates elicitation schema (static response)", Schema(new { type = "object", properties = new { } })),
        new(ToolNames.McpAppsHelloWorld, "Demonstrates MCP Apps UI reference", Schema(new { type = "object", properties = new { } }),
            new Dictionary<string, string> { ["ui/resourceUri"] = ResourceUris.HelloWorldApp })
    };
}

static IReadOnlyList<ResourceDefinition> ResourceDefinitions()
{
    var list = new List<ResourceDefinition>();
    for (var i = 1; i <= 10; i++)
    {
        var uri = $"test://static/resource/{i}";
        if (i % 2 == 0)
        {
            list.Add(new ResourceDefinition(uri, $"Resource {i}", "text/plain", text: $"Resource {i}: This is a plaintext resource"));
        }
        else
        {
            var blob = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"Resource {i}: This is a base64 blob"));
            list.Add(new ResourceDefinition(uri, $"Resource {i}", "application/octet-stream", blob: blob));
        }
    }

    list.Add(new ResourceDefinition(ResourceUris.HelloWorldApp, "Hello World MCP App", "text/html;profile=mcp-app", text: "<html><body>Hello MCP App</body></html>"));
    return list;
}

static object CallEcho(JsonElement? args)
{
    var message = args?.GetPropertyOrDefault("message")?.GetString() ?? string.Empty;
    return new
    {
        content = new object[]
        {
            new { type = "text", text = $"Echo: {message}" }
        }
    };
}

static object? CallAdd(JsonElement? args)
{
    if (args is null) return null;
    var a = args.Value.GetPropertyOrDefault("a")?.GetDouble() ?? 0;
    var b = args.Value.GetPropertyOrDefault("b")?.GetDouble() ?? 0;
    return new
    {
        content = new object[]
        {
            new { type = "text", text = $"The sum of {a} and {b} is {a + b}." }
        }
    };
}

static async Task<object?> CallLongRunning(JsonElement? args)
{
    var duration = args?.GetPropertyOrDefault("duration")?.GetDouble() ?? 10;
    var steps = (int)(args?.GetPropertyOrDefault("steps")?.GetDouble() ?? 5);
    steps = Math.Max(1, steps);
    var stepDuration = duration / steps;

    for (var i = 1; i <= steps; i++)
    {
        await Task.Delay(TimeSpan.FromSeconds(stepDuration));
    }

    return new
    {
        content = new object[]
        {
            new { type = "text", text = $"Long running operation completed. Duration: {duration} seconds, Steps: {steps}." }
        }
    };
}

static object CallSampleLLM(JsonElement? args)
{
    var prompt = args?.GetPropertyOrDefault("prompt")?.GetString() ?? string.Empty;
    return new
    {
        content = new object[]
        {
            new { type = "text", text = $"LLM sampling result: [stubbed] {prompt}" }
        }
    };
}

static object CallGetTinyImage()
{
    return new
    {
        content = new object[]
        {
            new { type = "text", text = "This is a tiny image:" },
            new { type = "image", data = McpTinyImage, mimeType = "image/png" },
            new { type = "text", text = "The image above is the MCP tiny image." }
        }
    };
}

static object? CallResourceReference(JsonElement? args, IReadOnlyList<ResourceDefinition> resources)
{
    var resourceId = (int)(args?.GetPropertyOrDefault("resourceId")?.GetDouble() ?? 0);
    var resourceIndex = resourceId - 1;
    if (resourceIndex < 0 || resourceIndex >= resources.Count) return null;

    var resource = resources[resourceIndex];
    return new
    {
        content = new object[]
        {
            new { type = "text", text = $"Returning resource reference for Resource {resourceId}:" },
            new { type = "resource", resource },
            new { type = "text", text = $"You can access this resource using the URI: {resource.Uri}" }
        }
    };
}

static object CallAnnotatedMessage(JsonElement? args)
{
    var messageType = args?.GetPropertyOrDefault("messageType")?.GetString() ?? "debug";
    var includeImage = args?.GetPropertyOrDefault("includeImage")?.GetBoolean() ?? false;

    var content = new List<object>();
    if (messageType == "error")
    {
        content.Add(new
        {
            type = "text",
            text = "Error: Operation failed",
            annotations = new { priority = 1.0, audience = new[] { "user", "assistant" } }
        });
    }
    else if (messageType == "success")
    {
        content.Add(new
        {
            type = "text",
            text = "Operation completed successfully",
            annotations = new { priority = 0.7, audience = new[] { "user" } }
        });
    }
    else
    {
        content.Add(new
        {
            type = "text",
            text = "Debug: Cache hit ratio 0.95, latency 150ms",
            annotations = new { priority = 0.3, audience = new[] { "assistant" } }
        });
    }

    if (includeImage)
    {
        content.Add(new
        {
            type = "image",
            data = McpTinyImage,
            mimeType = "image/png",
            annotations = new { priority = 0.5, audience = new[] { "user" } }
        });
    }

    return new { content };
}

static object CallElicitInputs()
{
    var schema = new
    {
        type = "object",
        properties = new
        {
            name = new { title = "Full Name", type = "string", description = "Your full, legal name" },
            check = new { title = "Agree to terms", type = "boolean", description = "A boolean check" },
            color = new { title = "Favorite Color", type = "string", description = "Favorite color", @default = "blue" }
        },
        required = new[] { "name" }
    };

    return new
    {
        content = new object[]
        {
            new { type = "text", text = $"Elicitation schema: {JsonSerializer.Serialize(schema)}" }
        }
    };
}


static object CallMcpAppsHelloWorld()
{
    return new
    {
        content = new object[]
        {
            new { type = "text", text = "If this client supports MCP Apps, an interactive UI should render for the user." }
        },
        _meta = new Dictionary<string, string> { ["ui/resourceUri"] = ResourceUris.HelloWorldApp }
    };
}

record ToolDefinition(string name, string description, JsonElement inputSchema, [property: JsonPropertyName("_meta")] Dictionary<string, string>? Meta = null);

record ResourceDefinition(string Uri, string Name, string MimeType, string? text = null, string? blob = null);

static class ToolNames
{
    public const string Echo = "echo";
    public const string Add = "add";
    public const string LongRunningOperation = "longRunningOperation";
    public const string SampleLLM = "sampleLLM";
    public const string GetTinyImage = "getTinyImage";
    public const string AnnotatedMessage = "annotatedMessage";
    public const string GetResourceReference = "getResourceReference";
    public const string ElicitInputs = "elicitInputs";
    public const string McpAppsHelloWorld = "mcp_apps_hello_world";
}

static class ResourceUris
{
    public const string HelloWorldApp = "ui://hello-world/app.html";
}

static class JsonElementExtensions
{
    public static JsonElement? GetPropertyOrDefault(this JsonElement element, string name)
    {
        return element.TryGetProperty(name, out var value) ? value : null;
    }
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
