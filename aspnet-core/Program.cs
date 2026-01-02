using AspNetMcpServer;
using Microsoft.AspNetCore.WebUtilities;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;

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
var resources = Enumerable.Range(1, 10).Select(i => new Resource
{
    Name = $"resource-{i}",
    Title = $"Sample Resource {i}",
    Uri = $"test://static/resource/{i}",
    Description = "Example MCP resource served by ASP.NET Core",
    MimeType = i % 2 == 0 ? "text/plain" : "application/octet-stream"
}).ToList();

var resourceTemplates = new List<ResourceTemplate>
{
    new()
    {
        Name = "static-resource",
        Title = "Static resource by id",
        UriTemplate = "test://static/resource/{id}",
        Description = "Templated static resource that can be read directly by id",
        MimeType = "text/plain"
    },
    new()
    {
        Name = "log-stream",
        Title = "Real-time log stream",
        UriTemplate = "test://logs/{level}",
        Description = "Subscribe to receive log messages for a level",
        MimeType = "text/plain"
    }
};

var resourceSubscriptions = new ConcurrentDictionary<string, byte>();

builder.Services.AddSingleton(serverOptions);
builder.Services.AddSingleton<InMemoryAuthStore>();

// Register MCP server with HTTP transport and attribute-discovered tools
builder.Services.AddMcpServer()
    .WithHttpTransport()
    .WithToolsFromAssembly()
    .WithListResourcesHandler(ListResourcesAsync)
    .WithListResourceTemplatesHandler(ListResourceTemplatesAsync)
    .WithReadResourceHandler(ReadResourceAsync)
    .WithSubscribeToResourcesHandler(SubscribeResourceAsync)
    .WithUnsubscribeFromResourcesHandler(UnsubscribeResourceAsync);

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

// Enforce bearer auth for MCP endpoints
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/mcp", StringComparison.OrdinalIgnoreCase))
    {
        if (!TryAuthenticate(context, context.RequestServices.GetRequiredService<InMemoryAuthStore>(), out _, out var failure))
        {
            await failure.ExecuteAsync(context);
            return;
        }
    }

    await next();
});

// Map MCP endpoints via the SDK
app.MapMcp();

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

ValueTask<ListResourcesResult> ListResourcesAsync(RequestContext<ListResourcesRequestParams> context, CancellationToken cancellationToken)
{
    const int pageSize = 5;
    var start = int.TryParse(context.Params?.Cursor, out var cursor) ? cursor : 0;
    var page = resources.Skip(start).Take(pageSize).ToList();
    var nextCursor = start + page.Count < resources.Count ? (start + page.Count).ToString() : null;

    return ValueTask.FromResult(new ListResourcesResult
    {
        Resources = page,
        NextCursor = nextCursor
    });
}

ValueTask<ListResourceTemplatesResult> ListResourceTemplatesAsync(RequestContext<ListResourceTemplatesRequestParams> context, CancellationToken cancellationToken)
{
    const int pageSize = 5;
    var start = int.TryParse(context.Params?.Cursor, out var cursor) ? cursor : 0;
    var page = resourceTemplates.Skip(start).Take(pageSize).ToList();
    var nextCursor = start + page.Count < resourceTemplates.Count ? (start + page.Count).ToString() : null;

    return ValueTask.FromResult(new ListResourceTemplatesResult
    {
        ResourceTemplates = page,
        NextCursor = nextCursor
    });
}

ValueTask<ReadResourceResult> ReadResourceAsync(RequestContext<ReadResourceRequestParams> context, CancellationToken cancellationToken)
{
    var uri = context.Params?.Uri ?? string.Empty;
    var resource = resources.FirstOrDefault(r => string.Equals(r.Uri, uri, StringComparison.OrdinalIgnoreCase));

    if (resource == null && uri.StartsWith("test://static/resource/", StringComparison.OrdinalIgnoreCase))
    {
        resource = new Resource
        {
            Name = uri.Split('/').Last(),
            Title = $"Generated {uri}",
            Uri = uri,
            Description = "Templated resource generated on demand",
            MimeType = "text/plain"
        };
    }

    if (resource == null)
    {
        return ValueTask.FromResult(new ReadResourceResult { Contents = Array.Empty<ResourceContents>() });
    }

    ResourceContents content = resource.MimeType?.StartsWith("text/", StringComparison.OrdinalIgnoreCase) == true
        ? new TextResourceContents { Uri = resource.Uri, MimeType = resource.MimeType, Text = $"Content for {resource.Title}" }
        : new BlobResourceContents { Uri = resource.Uri, MimeType = resource.MimeType ?? "application/octet-stream", Blob = Convert.ToBase64String(Encoding.UTF8.GetBytes($"Binary payload for {resource.Title}")) };

    return ValueTask.FromResult(new ReadResourceResult
    {
        Contents = new List<ResourceContents> { content }
    });
}

ValueTask<EmptyResult> SubscribeResourceAsync(RequestContext<SubscribeRequestParams> context, CancellationToken cancellationToken)
{
    var uri = context.Params?.Uri;
    if (!string.IsNullOrWhiteSpace(uri))
    {
        resourceSubscriptions[uri] = 1;
    }

    return ValueTask.FromResult(new EmptyResult());
}

ValueTask<EmptyResult> UnsubscribeResourceAsync(RequestContext<UnsubscribeRequestParams> context, CancellationToken cancellationToken)
{
    var uri = context.Params?.Uri;
    if (!string.IsNullOrWhiteSpace(uri))
    {
        resourceSubscriptions.TryRemove(uri, out _);
    }

    return ValueTask.FromResult(new EmptyResult());
}

static string SplashPage(ServerOptions options)
{
    return $@"<!DOCTYPE html>
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
  </body>
</html>";
}
