namespace AspNetMcpServer;

// Redis 中存储的 DTO
public class RedisSessionDto
{
    public string SessionId { get; set; }
    public string UserId { get; set; }
    public List<string> Subscriptions { get; set; } // 存储 URI 列表
    public Dictionary<string, string> MetaData { get; set; } // 存储连接元数据
}
