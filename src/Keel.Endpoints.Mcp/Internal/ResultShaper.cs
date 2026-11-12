using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Keel.Endpoints.Mcp.Internal;

/// <summary>How a tool's results are written, resolved from <see cref="KeelMcpOptions.Results"/> and the tool's options.</summary>
internal sealed record ResultShape(
    bool Compact,
    int? MaxItems,
    IReadOnlyList<string>? Fields,
    McpResultFormat Format,
    IReadOnlySet<string> Required,
    IReadOnlyList<string> Narrowing)
{
    public static readonly ResultShape Verbatim = new(false, null, null, McpResultFormat.Json, new HashSet<string>(), []);

    /// <summary>Nothing to do: the endpoint's body goes to the model as it is.</summary>
    public bool IsVerbatim => !Compact && MaxItems is null && Fields is null && Format == McpResultFormat.Json;
}

/// <summary>
/// Turns an endpoint's JSON body into a lean tool result. The model reads the text: only the selected members, no nulls,
/// a capped list, as compact JSON or a table. The structured content is for programs, and clients may validate it
/// against the output schema, so it keeps every member the schema requires (null or not selected) and stays JSON.
/// </summary>
internal static partial class ResultShaper
{
    /// <param name="Text">What the model reads.</param>
    /// <param name="Data">The shaped JSON that still matches the output schema, for structured content.</param>
    /// <param name="Note">Said to the agent after the result, when a list was cut.</param>
    public sealed record Shaped(string Text, JsonNode? Data, string? Note);

    private static readonly JsonSerializerOptions Lean = new()
    {
        WriteIndented = false,
        // The text is read by a model, never embedded in HTML: escaped characters would only cost tokens.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static readonly IReadOnlySet<string> Nothing = new HashSet<string>();

    public static Shaped Shape(JsonNode? root, ResultShape shape, IReadOnlyCollection<string>? fields)
    {
        string? note = null;
        if (shape.MaxItems is { } max && root is JsonArray list && list.Count > max)
        {
            var total = list.Count;
            var kept = new JsonArray();
            foreach (var item in list.Take(max)) kept.Add(item?.DeepClone());
            root = kept;
            note = $"Showing the first {max} of {total} items.";
            if (shape.Narrowing.Count > 0) note += $" To see the others, narrow the call with: {string.Join(", ", shape.Narrowing)}.";
        }

        // The model's copy drops everything it did not ask for; the structured copy keeps what the schema requires.
        var lean = root?.DeepClone();
        if (fields is { Count: > 0 })
        {
            lean = Select(lean, fields, Nothing);
            root = Select(root, fields, shape.Required);
        }

        if (shape.Compact)
        {
            Prune(lean, Nothing);
            Prune(root, shape.Required);
        }

        var text = shape.Format == McpResultFormat.Table && lean is JsonArray rows && Table(rows) is { } table
            ? table
            : lean?.ToJsonString(shape.Compact ? Lean : null) ?? "null";
        return new Shaped(text, root, note);
    }

    /// <summary>Keeps the requested members (and the required ones) of an object, or of each object in a list.</summary>
    private static JsonNode? Select(JsonNode? node, IReadOnlyCollection<string> fields, IReadOnlySet<string> required)
    {
        switch (node)
        {
            case JsonObject item:
                foreach (var name in item.Select(p => p.Key).ToList())
                {
                    if (!fields.Contains(name, StringComparer.OrdinalIgnoreCase) && !required.Contains(name)) item.Remove(name);
                }

                return item;
            case JsonArray list:
                foreach (var element in list) Select(element, fields, required);
                return list;
            default:
                return node;
        }
    }

    /// <summary>Removes null members, at any depth, except the ones the output schema requires.</summary>
    private static void Prune(JsonNode? node, IReadOnlySet<string> required)
    {
        switch (node)
        {
            case JsonObject item:
                foreach (var (name, value) in item.ToList())
                {
                    if (value is null && !required.Contains(name)) item.Remove(name);
                    else Prune(value, required);
                }

                break;
            case JsonArray list:
                foreach (var element in list) Prune(element, required);
                break;
        }
    }

    /// <summary>
    /// A list of flat objects as a TOON tabular array: <c>[2]{id,customer}:</c>, then one indented line per item. Null
    /// when the list is not tabular (an item is not an object, or a value is an object or a list).
    /// </summary>
    internal static string? Table(JsonArray rows)
    {
        var columns = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            if (row is not JsonObject item) return null;
            foreach (var (name, value) in item)
            {
                if (value is JsonObject or JsonArray) return null;
                if (seen.Add(name)) columns.Add(name);
            }
        }

        var builder = new StringBuilder();
        builder.Append('[').Append(rows.Count.ToString(CultureInfo.InvariantCulture)).Append(']');
        if (columns.Count > 0) builder.Append('{').AppendJoin(',', columns.Select(Key)).Append('}');
        builder.Append(':');

        foreach (var row in rows.Cast<JsonObject>())
        {
            builder.Append("\n  ");
            for (var i = 0; i < columns.Count; i++)
            {
                if (i > 0) builder.Append(',');
                builder.Append(Cell(row[columns[i]]));
            }
        }

        return builder.ToString();
    }

    private static string Key(string name) => SafeKey().IsMatch(name) ? name : Quote(name);

    private static string Cell(JsonNode? value)
    {
        if (value is null) return "null";
        return value.GetValueKind() switch
        {
            JsonValueKind.String => Literal(value.GetValue<string>()),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Null => "null",
            _ => value.ToJsonString(),
        };
    }

    /// <summary>A string cell: bare when it cannot be read as anything else, quoted otherwise.</summary>
    private static string Literal(string text) =>
        text.Length == 0
        || text != text.Trim()
        || text is "true" or "false" or "null"
        || text.StartsWith('-')
        || LooksNumeric().IsMatch(text)
        || text.AsSpan().IndexOfAny(",\"\\:[]{}#\n\r\t") >= 0
        || text.Any(char.IsControl)
            ? Quote(text)
            : text;

    private static string Quote(string text)
    {
        var builder = new StringBuilder(text.Length + 2).Append('"');
        foreach (var c in text)
        {
            builder.Append(c switch
            {
                '"' => "\\\"",
                '\\' => "\\\\",
                '\n' => "\\n",
                '\r' => "\\r",
                '\t' => "\\t",
                _ => c.ToString(),
            });
        }

        return builder.Append('"').ToString();
    }

    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_.]*$", RegexOptions.CultureInvariant)]
    private static partial Regex SafeKey();

    [GeneratedRegex(@"^-?(?:\d+\.?\d*|\.\d+)(?:[eE][+-]?\d+)?$|^0\d", RegexOptions.CultureInvariant)]
    private static partial Regex LooksNumeric();
}
