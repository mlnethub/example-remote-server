using Microsoft.Extensions.Primitives;

namespace AspNetMcpServer;

public  interface IMcpSessionFeature
{
    StringValues SessionId { get; set; }
    string UserId { get; set; }
    List<string> Subscriptions { get; set; }
}