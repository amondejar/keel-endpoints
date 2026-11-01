using Keel.Endpoints.Mcp;
using Keel.Endpoints.Mcp.Internal;
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
        if (options.Group is { } group)
        {
            throw new InvalidOperationException(
                $"Keel.Endpoints.Mcp: ExposeAsTool is called in the endpoint group {group.FullName}, which would expose every endpoint " +
                "it covers as one tool name. Call it in each endpoint's own Configure.");
        }

        var tool = new McpToolOptions();
        configure?.Invoke(tool);
        var metadata = new McpToolMetadata(description.Trim(), tool);
        return options.Convention(b =>
        {
            b.WithMetadata(metadata);
            // Once every convention has run, the endpoint's name and method are final: resolve the tool from them.
            b.Finally(endpoint => endpoint.Metadata.Add(AgentTool.Resolve(metadata, endpoint.Metadata)));
        });
    }
}
