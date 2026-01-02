namespace AspNetMcpServer;

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
