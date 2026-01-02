using System.ComponentModel;
using ModelContextProtocol.Server;

namespace AspNetMcpServer;

[McpServerToolType]
public static class Tools
{
    [McpServerTool, Description("Echoes back the input")]
    public static string Echo([Description("Message to echo")] string message) => $"Echo: {message}";

    [McpServerTool, Description("Adds two numbers")]
    public static string Add([Description("First number")] double a, [Description("Second number")] double b)
        => $"The sum of {a} and {b} is {a + b}.";

    [McpServerTool, Description("Demonstrates a long running operation with progress updates")]
    public static async Task<string> LongRunningOperation(
        [Description("Duration in seconds")] double duration = 10,
        [Description("Number of steps")] int steps = 5)
    {
        steps = Math.Max(1, steps);
        var stepDuration = duration / steps;
        for (var i = 0; i < steps; i++)
        {
            await Task.Delay(TimeSpan.FromSeconds(stepDuration));
        }

        return $"Long running operation completed. Duration: {duration} seconds, Steps: {steps}.";
    }

    [McpServerTool, Description("Samples from an LLM (stubbed response)")]
    public static string SampleLLM([Description("Prompt to send")] string prompt, [Description("Maximum tokens")] int maxTokens = 100)
        => $"LLM sampling result: [stubbed] {prompt}";

    [McpServerTool, Description("Returns the MCP tiny image (base64 text)")]
    public static string GetTinyImage()
        => "This is a tiny image: (base64 omitted in strong-typed sample)";

    [McpServerTool, Description("Demonstrates annotations on messages")]
    public static string AnnotatedMessage([Description("Type of message")] string messageType = "debug", [Description("Include image")] bool includeImage = false)
        => messageType switch
        {
            "error" => "Error: Operation failed",
            "success" => "Operation completed successfully",
            _ => "Debug: Cache hit ratio 0.95, latency 150ms"
        } + (includeImage ? " (image included)" : string.Empty);

    [McpServerTool, Description("Returns a resource reference (stubbed)")]
    public static string GetResourceReference([Description("ID of the resource to reference (1-100)")] int resourceId)
        => $"Returning resource reference for Resource {resourceId}: test://static/resource/{resourceId}";

    [McpServerTool, Description("Demonstrates elicitation schema (static response)")]
    public static string ElicitInputs() => "Elicitation schema requested (see client UI).";

    [McpServerTool, Description("Demonstrates MCP Apps UI reference")]
    public static string McpAppsHelloWorld() => "If this client supports MCP Apps, an interactive UI should render for the user.";
}
