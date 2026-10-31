using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;

namespace Keel.Endpoints.Mcp.Internal;

/// <summary>An exposed endpoint as a tool, resolved from its final metadata: the name and hints agents see.</summary>
internal sealed class AgentTool : IAgentToolMetadata
{
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required string Method { get; init; }
    public required bool ReadOnly { get; init; }
    public bool? Destructive { get; init; }
    public required bool Idempotent { get; init; }
    public required bool OpenWorld { get; init; }

    public static AgentTool Resolve(McpToolMetadata exposed, IEnumerable<object> metadata)
    {
        var list = metadata as IList<object> ?? [.. metadata];
        var method = list.OfType<IHttpMethodMetadata>().LastOrDefault()?.HttpMethods.FirstOrDefault()?.ToUpperInvariant() ?? "GET";
        var operation = list.OfType<IEndpointNameMetadata>().LastOrDefault()?.EndpointName
            ?? list.OfType<KeelEndpointMetadata>().FirstOrDefault()?.EndpointType.Name
            ?? "tool";
        var readOnly = method is "GET" or "HEAD";
        return new AgentTool
        {
            Name = exposed.Options.Name ?? ToolNames.SnakeCase(operation),
            Description = exposed.Description,
            Method = method,
            ReadOnly = readOnly,
            Destructive = readOnly ? null : exposed.Options.Destructive,
            Idempotent = readOnly || (exposed.Options.Idempotent ?? method is "PUT" or "DELETE"),
            OpenWorld = exposed.Options.OpenWorld,
        };
    }
}
