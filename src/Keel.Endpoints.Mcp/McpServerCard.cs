namespace Keel.Endpoints.Mcp;

/// <summary>
/// The MCP Server Card (SEP-2127): public metadata that lets a client or registry discover the server and how to
/// connect before it connects. Served at <c>&lt;mcp-url&gt;/server-card</c> when set on
/// <see cref="KeelMcpOptions.ServerCard"/>. It lists no tools: those depend on who asks, and agents list them at runtime.
/// </summary>
public sealed class McpServerCard
{
    /// <summary>
    /// The server name in reverse-DNS form, with one slash: <c>com.example/invoices</c>. Use a domain you control. Also
    /// the name the server reports at runtime, so the card and the live connection agree.
    /// </summary>
    public required string Name { get; set; }

    /// <summary>What the server is for, in up to 100 characters.</summary>
    public required string Description { get; set; }

    /// <summary>A display name, up to 100 characters.</summary>
    public string? Title { get; set; }

    /// <summary>The server version. Default: the application's informational version, without build metadata.</summary>
    public string? Version { get; set; }

    /// <summary>A page about the server: its documentation or the product's website.</summary>
    public string? WebsiteUrl { get; set; }

    /// <summary>The source repository, for clients that let users inspect it (<c>https://github.com/org/repo</c>).</summary>
    public string? RepositoryUrl { get; set; }

    /// <summary>
    /// The public URL of the MCP endpoint, as clients reach it (behind a proxy or a gateway it differs from what the
    /// application sees). Default: built from the request for the card, so configure <c>AllowedHosts</c> in production.
    /// </summary>
    public Uri? Url { get; set; }

    /// <summary>MCP protocol versions to advertise (<c>2025-11-25</c>...). Empty by default: clients negotiate when they connect.</summary>
    public IList<string> ProtocolVersions { get; } = [];

    /// <summary>
    /// Also publish an AI Catalog at <c>/.well-known/ai-catalog.json</c> pointing to the card, for domain-level discovery.
    /// Leave it off when something else on the domain publishes the catalog.
    /// </summary>
    public bool PublishAiCatalog { get; set; }
}
