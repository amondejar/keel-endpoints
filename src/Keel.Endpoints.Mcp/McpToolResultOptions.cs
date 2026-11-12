namespace Keel.Endpoints.Mcp;

/// <summary>
/// How tool results are written for the model. Every token of a result is read again on each later turn of the
/// conversation, so a lean result is cheaper for the whole session, not only for one call.
/// Set the defaults in <see cref="KeelMcpOptions.Results"/>; a tool can override them in its <c>ExposeAsTool</c>.
/// </summary>
public sealed class McpToolResultOptions
{
    /// <summary>
    /// Write the text the model reads without null members and without indentation. Structured content keeps the
    /// members the output schema requires, even when null. Default <see langword="true"/>.
    /// </summary>
    public bool Compact { get; set; } = true;

    /// <summary>
    /// The most items a list result returns. A longer list is cut, and the agent is told how many items there were and
    /// which arguments narrow the call. Default <see langword="null"/>: no limit.
    /// </summary>
    public int? MaxItems { get; set; }

    /// <summary>
    /// Adds an optional <c>fields</c> argument to tools with an object (or list of objects) result, so the agent asks
    /// only for the members it needs. Required members are always returned. Default <see langword="false"/>.
    /// </summary>
    public bool FieldSelection { get; set; }

    /// <summary>
    /// How the model reads list results. <see cref="McpResultFormat.Table"/> is smaller but not every model reads it
    /// as reliably as JSON: measure before turning it on. Default <see cref="McpResultFormat.Json"/>.
    /// </summary>
    public McpResultFormat Format { get; set; } = McpResultFormat.Json;
}

/// <summary>The text format of tool results for the model. Structured content is always JSON.</summary>
public enum McpResultFormat
{
    /// <summary>JSON.</summary>
    Json,

    /// <summary>
    /// Lists of flat objects as a table: the member names once, then one line per item
    /// (<see href="https://github.com/toon-format/spec">TOON</see> tabular arrays). Results that are not flat lists stay JSON.
    /// </summary>
    Table,
}
