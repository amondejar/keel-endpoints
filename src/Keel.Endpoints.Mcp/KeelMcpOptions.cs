namespace Keel.Endpoints.Mcp;

/// <summary>Options of the MCP server built from the Keel endpoints (<c>AddKeelMcp</c>).</summary>
public sealed class KeelMcpOptions
{
    /// <summary>
    /// Lists to each caller only the tools its identity is allowed to call, using the endpoints' authorization. Default
    /// <see langword="true"/>: an agent does not see, and so does not try, tools it cannot use. Calls are authorized
    /// either way.
    /// </summary>
    public bool ListAuthorizedToolsOnly { get; set; } = true;

    /// <summary>
    /// The MCP Server Card to publish at <c>&lt;mcp-url&gt;/server-card</c>, for discovery before connecting. Not
    /// published by default.
    /// </summary>
    public McpServerCard? ServerCard { get; set; }
}
