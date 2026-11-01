namespace Keel.Endpoints.Mcp.Internal;

/// <summary>Shared with Keel.Endpoints.Agents, which runs as a filter before the tool call.</summary>
internal static class McpToolCall
{
    /// <summary>
    /// A <c>RequestContext.Items</c> entry (<c>IReadOnlyDictionary&lt;string, string&gt;</c>): extra headers for the
    /// endpoint request the call builds, such as an <c>Idempotency-Key</c>.
    /// </summary>
    public const string HeadersItem = "Keel.Endpoints.Mcp.RequestHeaders";

    /// <summary>Set by Keel.Endpoints.Idempotency on a stored response.</summary>
    public const string ReplayedHeader = "Idempotent-Replayed";
}
