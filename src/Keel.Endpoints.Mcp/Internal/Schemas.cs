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

        var existing = schema["required"] as JsonArray;
        var required = existing ?? [];
        var names = required.Select(n => n?.GetValue<string>()).ToHashSet(StringComparer.Ordinal);
        foreach (var property in request?.Properties ?? [])
        {
            var routeValue = routeParameters.Contains(property.Name, StringComparer.OrdinalIgnoreCase);
            if ((property.IsRequired || routeValue || Attributes(property).OfType<RequiredAttribute>().Any()) && names.Add(property.Name))
            {
                required.Add(JsonValue.Create(property.Name));
            }
        }

        if (existing is null && required.Count > 0) schema["required"] = required;
        return ToElement(schema);
    }

    public static JsonElement? Output(JsonTypeInfo response) =>
        JsonSchemaExporter.GetJsonSchemaAsNode(response, Exporter) is { } schema ? ToElement(schema) : null;

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
