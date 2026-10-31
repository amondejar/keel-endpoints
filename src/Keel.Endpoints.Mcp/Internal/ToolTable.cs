using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;

namespace Keel.Endpoints.Mcp.Internal;

/// <summary>An exposed endpoint, ready to list and to call.</summary>
internal sealed class McpTool
{
    public required string Name { get; init; }
    public required Tool Protocol { get; init; }
    public required RouteEndpoint Endpoint { get; init; }
    public required string Method { get; init; }
    public required bool UsesBody { get; init; }
    public required IReadOnlyList<string> RouteParameters { get; init; }

    /// <summary>JSON name → header name, for contract members marked <c>[FromHeader]</c>.</summary>
    public required IReadOnlyDictionary<string, string> Headers { get; init; }

    /// <summary>The JSON name of the member marked <c>[FromBody]</c> in a parameter-bound contract, if any.</summary>
    public string? BodyMember { get; init; }
}

/// <summary>
/// The tools: every Keel endpoint with <c>ExposeAsTool</c>, read once from the endpoint table. Building it checks the
/// rules (a write says whether it is destructive, names are unique, streams are not tools), so a mistake stops the host
/// at startup like Keel's own checks.
/// </summary>
internal sealed partial class ToolTable(EndpointDataSource dataSource, IOptions<JsonOptions> json)
{
    private readonly Lock _gate = new();
    private (IReadOnlyList<Endpoint> Source, IReadOnlyDictionary<string, McpTool> Tools)? _built;

    /// <summary>
    /// The tools of the current endpoint table, rebuilt when the table changes: the application's endpoints only reach
    /// the global data source when the request pipeline is built, after hosted services have started.
    /// </summary>
    public IReadOnlyDictionary<string, McpTool> Tools
    {
        get
        {
            var endpoints = dataSource.Endpoints;
            if (_built is { } built && ReferenceEquals(built.Source, endpoints)) return built.Tools;
            lock (_gate)
            {
                if (_built is { } again && ReferenceEquals(again.Source, endpoints)) return again.Tools;
                var tools = Build(endpoints);
                _built = (endpoints, tools);
                return tools;
            }
        }
    }

    private Dictionary<string, McpTool> Build(IReadOnlyList<Endpoint> endpoints)
    {
        var options = json.Value.SerializerOptions;
        var tools = new Dictionary<string, McpTool>(StringComparer.Ordinal);
        var errors = new List<string>();

        foreach (var endpoint in endpoints.OfType<RouteEndpoint>())
        {
            if (endpoint.Metadata.GetMetadata<McpToolMetadata>() is not { } exposed) continue;
            var keel = endpoint.Metadata.GetMetadata<KeelEndpointMetadata>();
            var resolved = endpoint.Metadata.GetMetadata<AgentTool>() ?? AgentTool.Resolve(exposed, endpoint.Metadata);
            var method = resolved.Method;
            var where = $"{keel?.EndpointType.FullName ?? endpoint.DisplayName} ({method} {endpoint.RoutePattern.RawText})";
            if (keel is null)
            {
                errors.Add($"{where} is exposed as an MCP tool but is not a Keel endpoint class.");
                continue;
            }

            var shape = EndpointShape.Of(keel.EndpointType);
            if (shape.IsStream)
            {
                errors.Add($"{where}: a stream endpoint cannot be an MCP tool yet. Expose an endpoint that returns the whole result.");
                continue;
            }

            var readOnly = resolved.ReadOnly;
            if (readOnly && exposed.Options.Destructive is not null)
            {
                errors.Add($"{where} is read-only ({method}): remove Destructive from its ExposeAsTool options.");
            }
            else if (!readOnly && exposed.Options.Destructive is null)
            {
                errors.Add($"{where} changes data, so its tool must say whether it is destructive: " +
                           "ExposeAsTool(\"...\", tool => tool.Destructive = false) if it only adds, or true if it may overwrite or delete. " +
                           "Agents use it to decide what to confirm with the user.");
            }

            var name = resolved.Name;
            if (!ToolNames.IsValid(name))
            {
                errors.Add($"{where}: '{name}' is not a valid MCP tool name (letters, digits, '_', '-' and '.', up to 128 characters).");
                continue;
            }

            if (tools.TryGetValue(name, out var other))
            {
                var first = other.Endpoint.Metadata.GetMetadata<KeelEndpointMetadata>()?.EndpointType.FullName;
                errors.Add($"{where} and {first} are both the MCP tool '{name}': give one of them another Name in ExposeAsTool.");
                continue;
            }

            JsonTypeInfo? request = shape.Request is null ? null : options.GetTypeInfo(shape.Request);
            var routeParameters = endpoint.RoutePattern.Parameters.Select(p => p.Name).ToList();
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string? bodyMember = null;
            foreach (var property in request?.Properties ?? [])
            {
                foreach (var attribute in Schemas.Attributes(property))
                {
                    if (attribute is IFromHeaderMetadata header) headers[property.Name] = header.Name ?? property.Name;
                    else if (attribute is IFromBodyMetadata) bodyMember = property.Name;
                }
            }

            var protocol = new Tool
            {
                Name = name,
                Title = exposed.Options.Title,
                Description = exposed.Description,
                InputSchema = Schemas.Input(request, routeParameters),
                OutputSchema = shape.Response is null ? null : Schemas.Output(options.GetTypeInfo(shape.Response)),
                Annotations = new ToolAnnotations
                {
                    Title = exposed.Options.Title,
                    ReadOnlyHint = readOnly,
                    DestructiveHint = readOnly ? null : exposed.Options.Destructive,
                    IdempotentHint = readOnly ? null : resolved.Idempotent,
                    OpenWorldHint = exposed.Options.OpenWorld,
                },
            };

            tools[name] = new McpTool
            {
                Name = name,
                Protocol = protocol,
                Endpoint = endpoint,
                Method = method,
                UsesBody = UsesBody(method, shape.Request),
                RouteParameters = routeParameters,
                Headers = headers,
                BodyMember = bodyMember,
            };
        }

        if (errors.Count > 0)
        {
            throw new InvalidOperationException("Keel.Endpoints.Mcp: " + string.Join(Environment.NewLine, errors));
        }

        return tools;
    }

    /// <summary>The same rule as Keel's binding: POST, PUT and PATCH read the body unless the contract says otherwise.</summary>
    private static bool UsesBody(string method, Type? requestType)
    {
        if (requestType is null) return false;
        if (requestType.GetCustomAttributes(typeof(BindRequestAttribute), inherit: false) is [BindRequestAttribute bind, ..])
            return bind.Binding == RequestBinding.Body;
        return method is "POST" or "PUT" or "PATCH";
    }
}

/// <summary>The request and response types of an endpoint class, from the base class it derives from.</summary>
internal readonly record struct EndpointShape(Type? Request, Type? Response, bool IsStream)
{
    public static EndpointShape Of(Type endpointType)
    {
        for (var type = endpointType.BaseType; type is not null; type = type.BaseType)
        {
            if (!type.IsGenericType) continue;
            var definition = type.GetGenericTypeDefinition();
            var arguments = type.GetGenericArguments();
            if (definition == typeof(Endpoint<,>)) return new(arguments[0], arguments[1], false);
            if (definition == typeof(Endpoint<>)) return new(arguments[0], null, false);
            if (definition == typeof(EndpointWithoutRequest<>)) return new(null, arguments[0], false);
            if (definition == typeof(ResultEndpoint<,>)) return new(arguments[0], ResultValue(arguments[1]), false);
            if (definition == typeof(StreamEndpoint<,>)) return new(arguments[0], arguments[1], true);
        }

        return new(null, null, false);
    }

    /// <summary>The body type of the success result in <c>Results&lt;Ok&lt;T&gt;, NotFound&gt;</c> (or <c>Created&lt;T&gt;</c>...).</summary>
    private static Type? ResultValue(Type result)
    {
        if (!result.IsGenericType) return null;
        var definition = result.GetGenericTypeDefinition();
        if (definition == typeof(Microsoft.AspNetCore.Http.HttpResults.Ok<>) ||
            definition == typeof(Microsoft.AspNetCore.Http.HttpResults.Created<>) ||
            definition == typeof(Microsoft.AspNetCore.Http.HttpResults.CreatedAtRoute<>) ||
            definition == typeof(Microsoft.AspNetCore.Http.HttpResults.Accepted<>) ||
            definition == typeof(Microsoft.AspNetCore.Http.HttpResults.AcceptedAtRoute<>))
        {
            return result.GetGenericArguments()[0];
        }

        return definition.FullName?.StartsWith("Microsoft.AspNetCore.Http.HttpResults.Results`", StringComparison.Ordinal) == true
            ? result.GetGenericArguments().Select(ResultValue).FirstOrDefault(t => t is not null)
            : null;
    }
}

internal static partial class ToolNames
{
    /// <summary><c>InvoicesGetV2</c> → <c>invoices_get_v2</c>.</summary>
    public static string SnakeCase(string name)
    {
        var builder = new StringBuilder(name.Length + 8);
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (!char.IsLetterOrDigit(c))
            {
                if (builder.Length > 0 && builder[^1] != '_') builder.Append('_');
                continue;
            }

            if (i > 0 && builder.Length > 0 && builder[^1] != '_')
            {
                var previous = name[i - 1];
                var startsWord = char.IsUpper(c) && (char.IsLower(previous) || char.IsDigit(previous) ||
                                                    (char.IsUpper(previous) && i + 1 < name.Length && char.IsLower(name[i + 1])));
                if (startsWord) builder.Append('_');
            }

            builder.Append(char.ToLowerInvariant(c));
        }

        return builder.ToString().Trim('_');
    }

    public static bool IsValid(string name) => ValidName().IsMatch(name);

    [GeneratedRegex("^[A-Za-z0-9_.-]{1,128}$")]
    private static partial Regex ValidName();
}
