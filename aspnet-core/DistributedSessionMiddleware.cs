namespace AspNetMcpServer;

public class DistributedSessionMiddleware
{
    private readonly RequestDelegate _next;

    public DistributedSessionMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, IDistributedMcpSessionStore sessionStore)
    {
        if (context.Request.Headers.TryGetValue("Mcp-Session-Id", out var sessionId))
        {
            // 尝试从 Redis 恢复会话
            var sessionData = await sessionStore.GetSessionAsync(sessionId);

            if (sessionData == null)
            {
                // Redis 中没有，说明会话已过期，拒绝请求
                context.Response.StatusCode = 404;
                return;
            }

            // 关键：将 Redis 中的数据注入到当前请求的上下文中
            // 这样后续的 MCP Tool Handler 就能知道当前用户是谁，订阅了什么
            var feature = new McpSessionFeature
            {
                SessionId = sessionId,
                UserId = sessionData.UserId,
                Subscriptions = sessionData.Subscriptions // 从 Redis Set 加载
            };
            context.Features.Set<IMcpSessionFeature>(feature);

            // 刷新 Redis 过期时间
            await sessionStore.TouchSessionAsync(sessionId);
        }

        await _next(context);
    }
}
