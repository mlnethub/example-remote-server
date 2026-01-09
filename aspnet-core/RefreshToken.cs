namespace AspNetMcpServer;

record RefreshToken(string Token, string ClientId, string UserId, string Scope, DateTimeOffset ExpiresAt);

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

record RegisterRequest(string? ClientName, string? RedirectUri);