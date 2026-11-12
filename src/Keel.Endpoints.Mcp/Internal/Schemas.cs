using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using System.Text.Json.Serialization.Metadata;

namespace Keel.Endpoints.Mcp.Internal;

/// <summary>
/// JSON Schemas for tools, from the contracts through System.Text.Json (the same names and types the endpoint binds),
/// with the DataAnnotations rules the endpoint validates: an agent sees <c>Range(1, 1000)</c> as minimum and maximum
/// before it calls, instead of learning it from a 400.
/// </summary>
internal static class Schemas
{
    private static readonly JsonSchemaExporterOptions Exporter = new()
    {
        TreatNullObliviousAsNonNullable = true,
        TransformSchemaNode = Annotate,
    };

    public static JsonElement Input(JsonTypeInfo? request, IReadOnlyList<string> routeParameters)
    {
        var schema = request is null
            ? new JsonObject { ["properties"] = new JsonObject() }
            : JsonSchemaExporter.GetJsonSchemaAsNode(request, Exporter) as JsonObject ?? new JsonObject();

        // A tool's input is always an object.
        schema["type"] = "object";

        // The exporter lists every constructor parameter as required. A member the agent may leave out is not: a nullable
        // one or one with a default (filters such as "string? Customer"), unless it is in the route, [Required] or
        // C# required. Listing those tells the agent it must send them, and it does, on every call.
        var names = (schema["required"] as JsonArray ?? []).Select(n => n?.GetValue<string>()).OfType<string>().ToHashSet(StringComparer.Ordinal);
        var properties = schema["properties"] as JsonObject;
        foreach (var property in request?.Properties ?? [])
        {
            var routeValue = routeParameters.Contains(property.Name, StringComparer.OrdinalIgnoreCase);
            if (property.IsRequired || routeValue || Attributes(property).OfType<RequiredAttribute>().Any())
            {
                names.Add(property.Name);
            }
            else if (property.AssociatedParameter is { HasDefaultValue: true }
                     || (properties?[property.Name] is JsonObject member && HasType(member, "null")))
            {
                names.Remove(property.Name);
            }
        }

        // Keep the declaration order of the members.
        var ordered = (request?.Properties ?? []).Select(p => p.Name).Where(names.Contains).ToList();
        ordered.AddRange(names.Except(ordered).Order(StringComparer.Ordinal));
        if (ordered.Count > 0) schema["required"] = new JsonArray([.. ordered.Select(n => (JsonNode)JsonValue.Create(n))]);
        else schema.Remove("required");
        return ToElement(schema);
    }

    public static JsonElement? Output(JsonTypeInfo response) =>
        JsonSchemaExporter.GetJsonSchemaAsNode(response, Exporter) is { } schema ? ToElement(schema) : null;

    public static bool IsObject(JsonElement schema) =>
        schema.ValueKind == JsonValueKind.Object && schema.TryGetProperty("type", out var type) && type.ValueKind switch
        {
            JsonValueKind.String => type.GetString() == "object",
            JsonValueKind.Array => type.EnumerateArray().Any(t => t.GetString() == "object") && type.EnumerateArray().All(t => t.GetString() is "object" or "null"),
            _ => false,
        };

    /// <summary>The argument field selection adds to a tool.</summary>
    public const string FieldsArgument = "fields";

    public static bool IsArray(JsonElement schema) => HasType(schema, "array");

    /// <summary>The members of the result: of the object, or of the objects in the list. Empty when unknown.</summary>
    public static IReadOnlyList<string> ItemProperties(JsonElement schema)
    {
        var item = IsArray(schema) && schema.TryGetProperty("items", out var items) ? items : schema;
        return item.ValueKind == JsonValueKind.Object && HasType(item, "object") && item.TryGetProperty("properties", out var properties)
            ? properties.EnumerateObject().Select(p => p.Name).ToList()
            : [];
    }

    /// <summary>Every member name some object in the schema requires: shaping never removes those.</summary>
    public static IReadOnlySet<string> RequiredNames(JsonElement schema)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        Collect(schema);
        return names;

        void Collect(JsonElement node)
        {
            switch (node.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (var property in node.EnumerateObject())
                    {
                        if (property.Name == "required" && property.Value.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var name in property.Value.EnumerateArray())
                                if (name.ValueKind == JsonValueKind.String) names.Add(name.GetString()!);
                        }
                        else
                        {
                            Collect(property.Value);
                        }
                    }

                    break;
                case JsonValueKind.Array:
                    foreach (var element in node.EnumerateArray()) Collect(element);
                    break;
            }
        }
    }

    /// <summary>The input schema with the optional <c>fields</c> argument: an array of the result's member names.</summary>
    public static JsonElement AddFields(JsonElement input, IReadOnlyList<string> names)
    {
        var schema = JsonNode.Parse(input.GetRawText())!.AsObject();
        if (schema["properties"] is not JsonObject properties) schema["properties"] = properties = new JsonObject();
        properties[FieldsArgument] = new JsonObject
        {
            ["type"] = "array",
            ["items"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray([.. names.Select(n => (JsonNode)JsonValue.Create(n))]) },
            ["description"] = "Only these members in the result. Ask only for what you need: a smaller result costs less on every later turn. All members when omitted.",
        };
        return ToElement(schema);
    }

    /// <summary>The arguments an agent can add to narrow a call: the optional ones, but <c>fields</c>.</summary>
    public static IReadOnlyList<string> Optional(JsonElement input)
    {
        if (!input.TryGetProperty("properties", out var properties) || properties.ValueKind != JsonValueKind.Object) return [];
        var required = input.TryGetProperty("required", out var list) && list.ValueKind == JsonValueKind.Array
            ? list.EnumerateArray().Select(n => n.GetString()).ToHashSet(StringComparer.Ordinal)
            : [];
        return properties.EnumerateObject()
            .Select(p => p.Name)
            .Where(n => !required.Contains(n) && n != FieldsArgument)
            .ToList();
    }

    private static bool HasType(JsonElement schema, string type) =>
        schema.ValueKind == JsonValueKind.Object && schema.TryGetProperty("type", out var value) && value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() == type,
            JsonValueKind.Array => value.EnumerateArray().Any(t => t.GetString() == type),
            _ => false,
        };

    /// <summary><c>{"type": "object", "properties": {"result": schema}, "required": ["result"]}</c>.</summary>
    public static JsonElement WrapResult(JsonElement schema) => ToElement(new JsonObject
    {
        ["type"] = "object",
        ["properties"] = new JsonObject { ["result"] = JsonNode.Parse(schema.GetRawText()) },
        ["required"] = new JsonArray((JsonNode)JsonValue.Create("result")),
    });

    /// <summary>The attributes of a contract member: on the property, and on the record's constructor parameter.</summary>
    public static IEnumerable<object> Attributes(JsonPropertyInfo property)
    {
        foreach (var attribute in property.AttributeProvider?.GetCustomAttributes(inherit: true) ?? []) yield return attribute;
        foreach (var attribute in property.AssociatedParameter?.AttributeProvider?.GetCustomAttributes(inherit: true) ?? []) yield return attribute;
    }

    private static JsonNode Annotate(JsonSchemaExporterContext context, JsonNode schema)
    {
        if (context.PropertyInfo is not { } property || schema is not JsonObject node) return schema;
        var isArray = HasType(node, "array");

        foreach (var attribute in Attributes(property))
        {
            switch (attribute)
            {
                case DescriptionAttribute { Description.Length: > 0 } description:
                    node["description"] = description.Description;
                    break;
                case RangeAttribute range:
                    if (Number(range.Minimum) is { } min) node[range.MinimumIsExclusive ? "exclusiveMinimum" : "minimum"] = min;
                    if (Number(range.Maximum) is { } max) node[range.MaximumIsExclusive ? "exclusiveMaximum" : "maximum"] = max;
                    break;
                case StringLengthAttribute length:
                    node["maxLength"] = length.MaximumLength;
                    if (length.MinimumLength > 0) node["minLength"] = length.MinimumLength;
                    break;
                case LengthAttribute length:
                    node[isArray ? "minItems" : "minLength"] = length.MinimumLength;
                    node[isArray ? "maxItems" : "maxLength"] = length.MaximumLength;
                    break;
                case MinLengthAttribute length:
                    node[isArray ? "minItems" : "minLength"] = length.Length;
                    break;
                case MaxLengthAttribute { Length: > 0 } length:
                    node[isArray ? "maxItems" : "maxLength"] = length.Length;
                    break;
                case RegularExpressionAttribute pattern:
                    node["pattern"] = pattern.Pattern;
                    break;
                case EmailAddressAttribute:
                    node["format"] = "email";
                    break;
                case UrlAttribute:
                    node["format"] = "uri";
                    break;
            }
        }

        return node;
    }

    private static bool HasType(JsonObject node, string type) => node["type"] switch
    {
        JsonValue value => value.TryGetValue<string>(out var s) && s == type,
        JsonArray values => values.Any(v => v is JsonValue value && value.TryGetValue<string>(out var s) && s == type),
        _ => false,
    };

    private static double? Number(object? value) => value switch
    {
        null => null,
        string text => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : null,
        IConvertible convertible => convertible.ToDouble(CultureInfo.InvariantCulture),
        _ => null,
    };

    private static JsonElement ToElement(JsonNode node)
    {
        using var document = JsonDocument.Parse(node.ToJsonString());
        return document.RootElement.Clone();
    }
}
