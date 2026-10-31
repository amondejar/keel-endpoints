namespace Keel.Endpoints.Mcp;

/// <summary>How an endpoint appears to agents as an MCP tool (see <c>ExposeAsTool</c>).</summary>
public sealed class McpToolOptions
{
    /// <summary>
    /// The tool name. By default the endpoint's operation name in snake case: <c>Invoices.Get</c> becomes
    /// <c>invoices_get</c>. Letters, digits, <c>_</c>, <c>-</c> and <c>.</c>, up to 128 characters.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>A human-readable title for the tool, shown by clients that display one.</summary>
    public string? Title { get; set; }

    /// <summary>
    /// Required for endpoints that change data (any method but <c>GET</c> and <c>HEAD</c>): <see langword="true"/> when
    /// the call may overwrite or delete something, <see langword="false"/> when it only adds. Agents and their users use
    /// it to decide what needs a confirmation. Not allowed on read-only endpoints.
    /// </summary>
    public bool? Destructive { get; set; }

    /// <summary>
    /// Whether calling the tool twice with the same arguments has no extra effect. By default <see langword="true"/> for
    /// <c>PUT</c> and <c>DELETE</c> and <see langword="false"/> for <c>POST</c> and <c>PATCH</c>, as in HTTP.
    /// </summary>
    public bool? Idempotent { get; set; }

    /// <summary>
    /// Whether the tool reaches an open world beyond this API (the web, other people's systems). Default
    /// <see langword="false"/>: the tool works on this API's own data.
    /// </summary>
    public bool OpenWorld { get; set; }
}

/// <summary>Endpoint metadata added by <c>ExposeAsTool</c>: the endpoint is offered to agents as an MCP tool.</summary>
public sealed record McpToolMetadata(string Description, McpToolOptions Options);
