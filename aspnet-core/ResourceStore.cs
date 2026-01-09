using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;
using System.Threading;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace AspNetMcpServer;

[McpServerResourceType]
static class ResourceStore
{
    private const string StaticResourceTemplate = "test://static/resource/{id}";
    private const string LogStreamTemplate = "test://logs/{level}";

    [Description("Templated static resource that can be read directly by id")]
    [McpServerResource(
        UriTemplate = StaticResourceTemplate,
        Name = "static-resource",
        Title = "Static resource by id",
        MimeType = "text/plain")]
    public static IEnumerable<ResourceContents> GetStaticResource(
        [Description("ID of the resource to read")] int id,
        CancellationToken cancellationToken = default)
    {
        var uri = StaticResourceTemplate.Replace("{id}", id.ToString());
        var title = $"Sample Resource {id}";

        if (id % 2 == 0)
        {
            yield return new TextResourceContents
            {
                Uri = uri,
                MimeType = "text/plain",
                Text = $"Content for {title}"
            };
        }
        else
        {
            var payload = $"Binary payload for {title}";
            var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(payload));
            yield return new BlobResourceContents
            {
                Uri = uri,
                MimeType = "application/octet-stream",
                Blob = encoded
            };
        }
    }

    [Description("Subscribe to receive log messages for a level")]
    [McpServerResource(
        UriTemplate = LogStreamTemplate,
        Name = "log-stream",
        Title = "Real-time log stream",
        MimeType = "text/plain")]
    public static IEnumerable<ResourceContents> GetLogStream(
        [Description("Log level to subscribe to")] string level,
        CancellationToken cancellationToken = default)
    {
        var uri = LogStreamTemplate.Replace("{level}", level);
        yield return new TextResourceContents
        {
            Uri = uri,
            MimeType = "text/plain",
            Text = $"Subscribed to log level '{level}'."
        };
    }
}
