using System.Globalization;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Keel.Endpoints.Mcp.Internal;

/// <summary>
/// Lists and calls the tools. A call runs the endpoint's own request delegate in process, on a request built from the
/// tool arguments with the caller's identity: binding, validation, filters and ProblemDetails are the endpoint's, and
/// nothing goes back out over the network with forwarded credentials. Authorization is checked first, with the
/// endpoint's policies, because it is middleware and not part of the delegate.
/// </summary>
internal sealed class ToolInvoker(ToolTable table, IOptions<KeelMcpOptions> options, ILogger<ToolInvoker> logger)
{
    public async ValueTask<ListToolsResult> ListAsync(RequestContext<ListToolsRequestParams> request, CancellationToken cancellationToken)
    {
        var caller = Caller(request);
        var tools = new List<Tool>();
        foreach (var tool in table.Tools.Values.OrderBy(t => t.Name, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!options.Value.ListAuthorizedToolsOnly || (await AuthorizeAsync(tool, caller)).Allowed) tools.Add(tool.Protocol);
        }

        return new ListToolsResult { Tools = tools };
    }

    public async ValueTask<CallToolResult> CallAsync(RequestContext<CallToolRequestParams> request, CancellationToken cancellationToken)
    {
        var name = request.Params?.Name ?? string.Empty;
        if (!table.Tools.TryGetValue(name, out var tool))
        {
            throw new McpProtocolException($"Unknown tool '{name}'.", McpErrorCode.InvalidParams);
        }

        var caller = Caller(request);
        var authorization = await AuthorizeAsync(tool, caller);
        if (!authorization.Allowed) return Error(authorization.Status, authorization.Message);

        var context = Request(tool, request.Params?.Arguments, caller, cancellationToken);
        try
        {
            await tool.Endpoint.RequestDelegate!(context);
            // Results write through the response's PipeWriter: complete it so everything is in the buffer.
            await context.Response.CompleteAsync();
        }
        catch (BadHttpRequestException ex)
        {
            // Development hosts throw binding failures instead of answering 400 (RouteHandlerOptions.ThrowOnBadRequest).
            return Error(ex.StatusCode, ex.Message);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // The agent gets what an HTTP client would get from an unhandled exception: no details.
            logger.LogError(ex, "MCP tool {Tool} failed", tool.Name);
            return Error(StatusCodes.Status500InternalServerError, "The operation failed with an unexpected error.");
        }

        return Result(tool, context);
    }

    /// <summary>The HTTP request the MCP message came in (stateless HTTP), or a stand-in with the message's identity.</summary>
    private static HttpContext Caller<TParams>(RequestContext<TParams> request)
    {
        var services = request.Services ?? throw new InvalidOperationException("The MCP request has no services.");
        if (services.GetService<IHttpContextAccessor>()?.HttpContext is { } http) return http;
        return new DefaultHttpContext
        {
            RequestServices = services,
            User = request.User ?? new ClaimsPrincipal(new ClaimsIdentity()),
        };
    }

    private readonly record struct Authorization(bool Allowed, int Status, string Message);

    private static async Task<Authorization> AuthorizeAsync(McpTool tool, HttpContext caller)
    {
        var metadata = tool.Endpoint.Metadata;
        if (metadata.GetMetadata<IAllowAnonymous>() is not null) return new(true, 200, string.Empty);

        var services = caller.RequestServices;
        var policy = await AuthorizationPolicy.CombineAsync(
            services.GetRequiredService<IAuthorizationPolicyProvider>(),
            metadata.GetOrderedMetadata<IAuthorizeData>(),
            metadata.GetOrderedMetadata<AuthorizationPolicy>());
        if (policy is null) return new(true, 200, string.Empty);

        var evaluator = services.GetRequiredService<IPolicyEvaluator>();
        var authentication = await evaluator.AuthenticateAsync(policy, caller);
        var result = await evaluator.AuthorizeAsync(policy, authentication, caller, resource: caller);
        if (result.Succeeded) return new(true, 200, string.Empty);
        return result.Challenged
            ? new(false, StatusCodes.Status401Unauthorized, "This tool needs an authenticated caller.")
            : new(false, StatusCodes.Status403Forbidden, "The caller is not allowed to use this tool.");
    }

    private static DefaultHttpContext Request(McpTool tool, IDictionary<string, JsonElement>? arguments, HttpContext caller, CancellationToken cancellationToken)
    {
        var context = new DefaultHttpContext
        {
            RequestServices = caller.RequestServices,
            User = caller.User,
            RequestAborted = cancellationToken,
        };

        var request = context.Request;
        request.Method = tool.Method;
        request.Protocol = "HTTP/1.1";
        request.Scheme = caller.Request.Scheme is { Length: > 0 } scheme ? scheme : "http";
        request.Host = caller.Request.Host.HasValue ? caller.Request.Host : new HostString("localhost");
        request.PathBase = caller.Request.PathBase;
        request.Headers.Accept = "application/json";
        if (caller.Request.Headers.AcceptLanguage.Count > 0) request.Headers.AcceptLanguage = caller.Request.Headers.AcceptLanguage;

        var args = arguments ?? new Dictionary<string, JsonElement>();
        var routeValues = new RouteValueDictionary();
        foreach (var parameter in tool.RouteParameters)
        {
            if (Find(args, parameter) is { } value) routeValues[parameter] = Text(value);
        }

        request.RouteValues = routeValues;
        request.Path = Path(tool, routeValues);

        if (tool.UsesBody)
        {
            Body(request, Json(args));
        }
        else
        {
            var query = new List<KeyValuePair<string, string?>>();
            foreach (var (key, value) in args)
            {
                if (tool.RouteParameters.Contains(key, StringComparer.OrdinalIgnoreCase)) continue;
                if (tool.Headers.TryGetValue(key, out var header)) request.Headers[header] = Text(value);
                else if (string.Equals(key, tool.BodyMember, StringComparison.OrdinalIgnoreCase)) Body(request, value.GetRawText());
                else if (value.ValueKind == JsonValueKind.Array) query.AddRange(value.EnumerateArray().Select(item => new KeyValuePair<string, string?>(key, Text(item))));
                else if (value.ValueKind != JsonValueKind.Null) query.Add(new(key, Text(value)));
            }

            if (query.Count > 0) request.QueryString = QueryString.Create(query);
        }

        context.Response.Body = new MemoryStream();
        context.SetEndpoint(tool.Endpoint);
        return context;
    }

    private static void Body(HttpRequest request, string json)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        request.ContentType = "application/json";
        request.ContentLength = bytes.Length;
        request.Body = new MemoryStream(bytes);
        // Minimal APIs read a body only when the server says the request can have one, as Kestrel does.
        request.HttpContext.Features.Set<IHttpRequestBodyDetectionFeature>(new HasBody());
    }

    private sealed class HasBody : IHttpRequestBodyDetectionFeature
    {
        public bool CanHaveBody => true;
    }

    private static CallToolResult Result(McpTool tool, HttpContext context)
    {
        var status = context.Response.StatusCode;
        var body = Encoding.UTF8.GetString(((MemoryStream)context.Response.Body).ToArray());
        if (status is < 200 or >= 300) return Error(status, body.Length > 0 ? body : ReasonPhrases.GetReasonPhrase(status));

        var result = new CallToolResult
        {
            Content = [new TextContentBlock { Text = body.Length > 0 ? body : $"{status} {ReasonPhrases.GetReasonPhrase(status)}" }],
        };

        if (body.Length > 0 && tool.Protocol.OutputSchema is not null && context.Response.ContentType?.Contains("json", StringComparison.OrdinalIgnoreCase) == true)
        {
            try
            {
                using var document = JsonDocument.Parse(body);
                result.StructuredContent = document.RootElement.Clone();
            }
            catch (JsonException)
            {
                // Not JSON after all: the text content has it.
            }
        }

        return result;
    }

    /// <summary>A failed call, as the agent sees it: the status and the endpoint's own answer (ProblemDetails).</summary>
    private static CallToolResult Error(int status, string detail) => new()
    {
        IsError = true,
        Content = [new TextContentBlock { Text = $"{status} {ReasonPhrases.GetReasonPhrase(status)}: {detail}" }],
    };

    private static JsonElement? Find(IDictionary<string, JsonElement> args, string name)
    {
        if (args.TryGetValue(name, out var exact)) return exact;
        foreach (var (key, value) in args)
        {
            if (string.Equals(key, name, StringComparison.OrdinalIgnoreCase)) return value;
        }

        return null;
    }

    /// <summary>A route, query or header value: strings as they are, numbers and booleans as JSON writes them.</summary>
    private static string Text(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString() ?? string.Empty,
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        JsonValueKind.Null => string.Empty,
        _ => value.GetRawText(),
    };

    private static string Json(IDictionary<string, JsonElement> args)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            foreach (var (key, value) in args)
            {
                writer.WritePropertyName(key);
                value.WriteTo(writer);
            }

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>The request path, for logs and telemetry: the route template with the values in it.</summary>
    private static string Path(McpTool tool, RouteValueDictionary values)
    {
        var builder = new StringBuilder();
        foreach (var segment in tool.Endpoint.RoutePattern.PathSegments)
        {
            builder.Append('/');
            foreach (var part in segment.Parts)
            {
                switch (part)
                {
                    case Microsoft.AspNetCore.Routing.Patterns.RoutePatternLiteralPart literal:
                        builder.Append(literal.Content);
                        break;
                    case Microsoft.AspNetCore.Routing.Patterns.RoutePatternParameterPart parameter:
                        builder.Append(Uri.EscapeDataString(Convert.ToString(values[parameter.Name], CultureInfo.InvariantCulture) ?? string.Empty));
                        break;
                    case Microsoft.AspNetCore.Routing.Patterns.RoutePatternSeparatorPart separator:
                        builder.Append(separator.Content);
                        break;
                }
            }
        }

        return builder.Length == 0 ? "/" : builder.ToString();
    }
}
