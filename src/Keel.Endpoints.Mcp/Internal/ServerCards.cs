using System.Reflection;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Keel.Endpoints.Mcp.Internal;

/// <summary>
/// Serves the MCP Server Card (SEP-2127) at <c>&lt;mcp-url&gt;/server-card</c> and, if asked, the AI Catalog at
/// <c>/.well-known/ai-catalog.json</c>: public, read-only, cacheable documents, with the CORS and caching headers the
/// discovery spec asks for.
/// </summary>
internal static partial class ServerCards
{
    public const string CardMediaType = "application/mcp-server-card+json";
    public const string CatalogMediaType = "application/ai-catalog+json";
    public const string CatalogPath = "/.well-known/ai-catalog.json";
    private const string Schema = "https://static.modelcontextprotocol.io/schemas/v1/server-card.schema.json";

    public static void Map(IEndpointRouteBuilder app, string mcpPattern, McpServerCard card)
    {
        Validate(card);
        var version = VersionOf(card);
        var cardPath = mcpPattern.TrimEnd('/') + "/server-card";

        MapDocument(app, cardPath, "MCP server card", CardMediaType, request => Card(card, version, McpUrl(card, request, mcpPattern)));
        if (card.PublishAiCatalog)
        {
            MapDocument(app, CatalogPath, "AI catalog", CatalogMediaType, request => Catalog(card, CardUrl(card, request, mcpPattern)));
        }
    }

    /// <summary>The name and version the server reports at runtime, so they match the card.</summary>
    public static Implementation ServerInfo(McpServerCard card) => new()
    {
        Name = card.Name,
        Version = VersionOf(card),
        Title = card.Title,
        Description = card.Description,
        WebsiteUrl = card.WebsiteUrl,
    };

    private static void MapDocument(IEndpointRouteBuilder app, string path, string name, string mediaType, Func<HttpRequest, byte[]> render)
    {
        app.MapMethods(path, [HttpMethods.Get, HttpMethods.Head], (RequestDelegate)(context => Serve(context, mediaType, render(context.Request))))
            .AllowAnonymous()
            .ExcludeFromDescription()
            .WithDisplayName(name);

        // Browsers ask before sending If-None-Match from another origin.
        app.MapMethods(path, [HttpMethods.Options], (RequestDelegate)(context =>
            {
                Cors(context.Response.Headers);
                context.Response.StatusCode = StatusCodes.Status204NoContent;
                return Task.CompletedTask;
            }))
            .AllowAnonymous()
            .ExcludeFromDescription()
            .WithDisplayName(name + " (CORS preflight)");
    }

    private static async Task Serve(HttpContext context, string mediaType, byte[] body)
    {
        var response = context.Response;
        var etag = "\"" + Convert.ToHexString(SHA256.HashData(body), 0, 16).ToLowerInvariant() + "\"";
        Cors(response.Headers);
        response.Headers.CacheControl = "public, max-age=3600";
        response.Headers.ETag = etag;

        if (Matches(context.Request.Headers.IfNoneMatch, etag))
        {
            response.StatusCode = StatusCodes.Status304NotModified;
            return;
        }

        response.ContentType = mediaType;
        response.ContentLength = body.Length;
        if (HttpMethods.IsHead(context.Request.Method)) return;
        await response.Body.WriteAsync(body, context.RequestAborted);
    }

    private static void Cors(IHeaderDictionary headers)
    {
        headers.AccessControlAllowOrigin = "*";
        headers.AccessControlAllowMethods = "GET";
        headers.AccessControlAllowHeaders = "Content-Type, If-None-Match";
        headers.AccessControlExposeHeaders = "ETag";
    }

    private static bool Matches(StringValues ifNoneMatch, string etag)
    {
        foreach (var value in ifNoneMatch)
        {
            foreach (var candidate in (value ?? string.Empty).Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                var tag = candidate.StartsWith("W/", StringComparison.Ordinal) ? candidate[2..] : candidate;
                if (tag == "*" || tag == etag) return true;
            }
        }

        return false;
    }

    private static byte[] Card(McpServerCard card, string version, string url) => Write(w =>
    {
        w.WriteString("$schema", Schema);
        w.WriteString("name", card.Name);
        w.WriteString("version", version);
        w.WriteString("description", card.Description.Trim());
        if (card.Title is { Length: > 0 } title) w.WriteString("title", title);
        if (card.WebsiteUrl is { Length: > 0 } website) w.WriteString("websiteUrl", website);
        if (card.RepositoryUrl is { Length: > 0 } repository)
        {
            w.WriteStartObject("repository");
            w.WriteString("url", repository);
            w.WriteString("source", Source(new Uri(repository)));
            w.WriteEndObject();
        }

        w.WriteStartArray("remotes");
        w.WriteStartObject();
        w.WriteString("type", "streamable-http");
        w.WriteString("url", url);
        if (card.ProtocolVersions.Count > 0)
        {
            w.WriteStartArray("supportedProtocolVersions");
            foreach (var protocol in card.ProtocolVersions) w.WriteStringValue(protocol);
            w.WriteEndArray();
        }

        w.WriteEndObject();
        w.WriteEndArray();
    });

    private static byte[] Catalog(McpServerCard card, string cardUrl) => Write(w =>
    {
        w.WriteString("specVersion", "1.0");
        w.WriteStartArray("entries");
        w.WriteStartObject();
        w.WriteString("identifier", Identifier(card.Name));
        w.WriteString("type", CardMediaType);
        w.WriteString("url", cardUrl);
        w.WriteEndObject();
        w.WriteEndArray();
    });

    private static byte[] Write(Action<Utf8JsonWriter> body)
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            w.WriteStartObject();
            body(w);
            w.WriteEndObject();
        }

        return stream.ToArray();
    }

    /// <summary><c>com.example/invoices</c> → <c>urn:air:example.com:mcp:invoices</c>, the catalog's domain-anchored form.</summary>
    internal static string Identifier(string name)
    {
        var slash = name.IndexOf('/');
        var publisher = string.Join('.', Enumerable.Reverse(name[..slash].Split('.')));
        return $"urn:air:{publisher}:mcp:{name[(slash + 1)..]}";
    }

    private static string McpUrl(McpServerCard card, HttpRequest request, string mcpPattern) =>
        card.Url?.AbsoluteUri.TrimEnd('/') ?? $"{request.Scheme}://{request.Host}{request.PathBase}/{mcpPattern.Trim('/')}";

    private static string CardUrl(McpServerCard card, HttpRequest request, string mcpPattern) =>
        McpUrl(card, request, mcpPattern) + "/server-card";

    private static string Source(Uri repository) => repository.Host.ToLowerInvariant() switch
    {
        "github.com" => "github",
        "gitlab.com" => "gitlab",
        "bitbucket.org" => "bitbucket",
        var host => host,
    };

    private static string VersionOf(McpServerCard card)
    {
        if (card.Version is { Length: > 0 } version) return version;
        var assembly = Assembly.GetEntryAssembly();
        var informational = assembly?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (informational is { Length: > 0 })
        {
            var plus = informational.IndexOf('+');
            return plus > 0 ? informational[..plus] : informational;
        }

        return assembly?.GetName().Version?.ToString(3) ?? "1.0.0";
    }

    /// <summary>The card's rules from the schema, checked when the endpoint is mapped: a bad card stops the host.</summary>
    private static void Validate(McpServerCard card)
    {
        var errors = new List<string>();
        if (card.Name is null || card.Name.Length is < 3 or > 200 || !NamePattern().IsMatch(card.Name))
            errors.Add($"Name '{card.Name}' must be reverse-DNS with one slash, such as com.example/invoices (3 to 200 characters).");
        if (string.IsNullOrWhiteSpace(card.Description) || card.Description.Trim().Length > 100)
            errors.Add("Description is required, up to 100 characters.");
        if (card.Title is { Length: 0 or > 100 })
            errors.Add("Title must have 1 to 100 characters.");
        if (card.Version is { } version && (version.Length is 0 or > 255 || RangePattern().IsMatch(version)))
            errors.Add($"Version '{version}' must be one version (1.2.0), not a range.");
        if (card.WebsiteUrl is { } website && !IsHttp(website)) errors.Add($"WebsiteUrl '{website}' must be an absolute http(s) URL.");
        if (card.RepositoryUrl is { } repository && !IsHttp(repository)) errors.Add($"RepositoryUrl '{repository}' must be an absolute http(s) URL.");
        if (card.Url is { } url && (!url.IsAbsoluteUri || url.Scheme is not ("http" or "https")))
            errors.Add($"Url '{url}' must be the absolute http(s) URL of the MCP endpoint.");

        if (errors.Count > 0)
        {
            throw new InvalidOperationException("Keel.Endpoints.Mcp: the MCP server card is not valid: " + string.Join(" ", errors));
        }
    }

    private static bool IsHttp(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https";

    [GeneratedRegex("^[a-zA-Z0-9.-]+/[a-zA-Z0-9._-]+$")]
    private static partial Regex NamePattern();

    [GeneratedRegex(@"^[\^~<>=]|(^|\.)[xX*](\.|$)|\|\||\s-\s")]
    private static partial Regex RangePattern();
}

/// <summary>Reports the card's name and version at runtime too, unless the application set its own server info.</summary>
internal sealed class ServerInfoFromCard(IOptions<KeelMcpOptions> keel) : IPostConfigureOptions<McpServerOptions>
{
    public void PostConfigure(string? name, McpServerOptions options)
    {
        if (options.ServerInfo is null && keel.Value.ServerCard is { } card) options.ServerInfo = ServerCards.ServerInfo(card);
    }
}
