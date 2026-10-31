using Keel.Endpoints.Mcp;
using Microsoft.AspNetCore.Builder;

namespace Keel.Endpoints;

/// <summary>Exposing an endpoint as an MCP tool, in its <c>Configure</c>.</summary>
public static class McpEndpointOptionsExtensions
{
    /// <summary>
    /// Offers the endpoint to AI agents as an MCP tool (requires <c>AddKeelMcp()</c> and <c>MapKeelMcp()</c>). The
    /// request contract is the tool's input schema and the response its output schema; each call runs this endpoint,
    /// with its binding, validation and authorization, as the calling user. Nothing is exposed without this call.
    /// </summary>
    /// <param name="options">The endpoint's options.</param>
    /// <param name="description">What the tool does and when to use it, for the agent: one or two sentences.</param>
    /// <param name="configure">Name, title and hints; endpoints that change data must set <see cref="McpToolOptions.Destructive"/>.</param>
    public static EndpointOptions ExposeAsTool(this EndpointOptions options, string description, Action<McpToolOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        var tool = new McpToolOptions();
        configure?.Invoke(tool);
        return options.Convention(b => b.WithMetadata(new McpToolMetadata(description.Trim(), tool)));
    }
}
