using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Keel.Endpoints.Agents.Internal;

/// <summary>The exposed tools by name, from the endpoint table: their hints and confirmation settings.</summary>
internal sealed class AgentToolIndex(EndpointDataSource dataSource)
{
    private readonly Lock _gate = new();
    private (IReadOnlyList<Endpoint> Source, Dictionary<string, Endpoint> Tools)? _built;

    public bool TryGet(string name, out Endpoint endpoint, out IAgentToolMetadata tool)
    {
        if (Tools().TryGetValue(name, out var found) && found.Metadata.GetMetadata<IAgentToolMetadata>() is { } metadata)
        {
            endpoint = found;
            tool = metadata;
            return true;
        }

        endpoint = null!;
        tool = null!;
        return false;
    }

    private Dictionary<string, Endpoint> Tools()
    {
        var endpoints = dataSource.Endpoints;
        if (_built is { } built && ReferenceEquals(built.Source, endpoints)) return built.Tools;
        lock (_gate)
        {
            var tools = new Dictionary<string, Endpoint>(StringComparer.Ordinal);
            foreach (var endpoint in endpoints)
            {
                if (endpoint.Metadata.GetMetadata<IAgentToolMetadata>() is { } tool) tools.TryAdd(tool.Name, endpoint);
            }

            _built = (endpoints, tools);
            return tools;
        }
    }
}
