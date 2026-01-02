using Microsoft.Extensions.Primitives;

namespace AspNetMcpServer;

public class McpSessionFeature : IMcpSessionFeature
{
    public StringValues SessionId { get; set; }
    public string UserId { get; set; }
    public List<string> Subscriptions { get; set; }
}