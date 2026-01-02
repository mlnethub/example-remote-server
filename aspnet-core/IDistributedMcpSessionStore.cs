using ModelContextProtocol.Protocol;

namespace AspNetMcpServer;

public interface IDistributedMcpSessionStore
{
    // 创建新会话
    Task RegisterSessionAsync(string sessionId, string userId, ClientCapabilities caps);

    // 获取会话信息（用于鉴权和恢复）
    Task<McpSessionContext?> GetSessionAsync(string sessionId);

    // 添加订阅
    Task AddSubscriptionAsync(string sessionId, string resourceUri);

    // 移除订阅
    Task RemoveSubscriptionAsync(string sessionId, string resourceUri);

    // 维持心跳/更新活跃时间
    Task TouchSessionAsync(string sessionId);

    // 获取某资源的所有订阅者 SessionID
    Task<IEnumerable<string>> GetSubscribersForResourceAsync(string resourceUri);
}
