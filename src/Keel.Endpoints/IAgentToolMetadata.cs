namespace Keel.Endpoints;

/// <summary>
/// Endpoint metadata for an endpoint offered to AI agents as a tool. <c>ExposeAsTool</c> (Keel.Endpoints.Mcp) adds it;
/// the API manifest (Keel.Endpoints.Testing) records it, so <c>dotnet keel diff</c> reports a renamed tool, a tool that
/// is no longer exposed, or a write that stopped declaring itself destructive.
/// </summary>
public interface IAgentToolMetadata
{
    /// <summary>The tool name agents call.</summary>
    string Name { get; }

    /// <summary>What the tool does and when to use it, as the agent reads it.</summary>
    string Description { get; }

    /// <summary>Whether the tool only reads (<c>GET</c> and <c>HEAD</c>).</summary>
    bool ReadOnly { get; }

    /// <summary>For a tool that changes data: whether it may overwrite or delete. <see langword="null"/> for read-only tools.</summary>
    bool? Destructive { get; }

    /// <summary>Whether calling the tool twice with the same arguments has no extra effect.</summary>
    bool Idempotent { get; }

    /// <summary>Whether the tool reaches beyond this API (the web, other people's systems).</summary>
    bool OpenWorld { get; }
}
