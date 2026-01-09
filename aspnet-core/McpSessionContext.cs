namespace AspNetMcpServer;

public class McpSessionContext
{
    public string SessionId { get; set; }

    public string UserId { get;  set; }

    public List<string> Subscriptions { get; set; }
}