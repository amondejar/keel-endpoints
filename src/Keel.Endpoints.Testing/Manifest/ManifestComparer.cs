using System.Text.RegularExpressions;

namespace Keel.Endpoints.Testing;

/// <summary>
/// Compares two manifests from the point of view of an existing client. What a client sends (route,
/// parameters, request body) may become more permissive but not stricter; what it reads (responses)
/// may gain members but not lose or change them.
/// </summary>
internal sealed partial class ManifestComparer(ApiManifest baseline, ApiManifest current)
{
    private readonly List<ApiChange> _changes = [];
    private readonly HashSet<(string, string, bool)> _visited = [];
    private string _endpoint = string.Empty;

    public IReadOnlyList<ApiChange> Run()
    {
        var currentByShape = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, e) in current.Endpoints) currentByShape.TryAdd(Shape(e), key);
        var baselineShapes = baseline.Endpoints.Values.Select(Shape).ToHashSet(StringComparer.Ordinal);
        var currentByOperation = current.Endpoints
            .Where(e => e.Value.Operation is not null)
            .GroupBy(e => e.Value.Operation!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().Key, StringComparer.Ordinal);

        var matched = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (key, old) in baseline.Endpoints)
        {
            _endpoint = key;
            if (!currentByShape.TryGetValue(Shape(old), out var newKey))
            {
                // Same operation under a URL the baseline did not have: the endpoint moved.
                if (old.Operation is not null && currentByOperation.TryGetValue(old.Operation, out var moved)
                    && !baselineShapes.Contains(Shape(current.Endpoints[moved])))
                {
                    Add(ApiChangeKind.Breaking, $"moved to {moved}");
                    newKey = moved;
                }
                else
                {
                    Add(ApiChangeKind.Breaking, "removed");
                    continue;
                }
            }

            matched.Add(newKey);
            CompareEndpoint(old, current.Endpoints[newKey]);
        }

        foreach (var key in current.Endpoints.Keys.Where(k => !matched.Contains(k)))
        {
            _endpoint = key;
            Add(ApiChangeKind.Compatible, "added");
            CompareTool(null, current.Endpoints[key].Tool);
        }

        return [.. _changes
            .OrderBy(c => c.Kind)
            .ThenBy(c => c.Endpoint, StringComparer.Ordinal)
            .ThenBy(c => c.Description, StringComparer.Ordinal)];
    }

    /// <summary>Method and URL shape: parameter names and constraints do not matter to clients.</summary>
    private static string Shape(ApiManifestEndpoint e) =>
        e.Method.ToUpperInvariant() + " " + RouteParameter().Replace(e.Route.Trim('/').ToLowerInvariant(), "{}");

    [GeneratedRegex(@"\{[^}]*\}")]
    private static partial Regex RouteParameter();

    private void CompareEndpoint(ApiManifestEndpoint old, ApiManifestEndpoint now)
    {
        if (old.Route != now.Route && Shape(old) == Shape(now))
            Add(ApiChangeKind.Compatible, $"route template changed to {now.Route} (same URL shape)");

        if (old.Operation != now.Operation && old.Operation is not null)
        {
            Add(ApiChangeKind.Compatible, now.Operation is null
                ? $"operation name {old.Operation} removed (generated clients may rename their method)"
                : $"operation name changed from {old.Operation} to {now.Operation} (generated clients may rename their method)");
        }

        CompareAuthorization(old, now);

        for (var i = 0; i < Math.Min(old.Path.Count, now.Path.Count); i++)
            CompareSpec($"route parameter '{now.Path[i].Key}'", Spec.Parse(old.Path[i].Value, required: true), Spec.Parse(now.Path[i].Value, required: true), request: true);

        CompareParameters("query parameter", old.Query, now.Query);
        CompareParameters("header", old.Header, now.Header);
        CompareParameters("form field", old.Form, now.Form);
        CompareBody(old.Body, now.Body);
        CompareResponses(old.Responses, now.Responses);
        CompareTool(old.Tool, now.Tool);
    }

    /// <summary>
    /// The endpoint as an MCP tool, from the point of view of an agent that uses it: its name is its address, and its
    /// hints decide what the agent asks the user to confirm and whether it retries.
    /// </summary>
    private void CompareTool(ApiManifestTool? old, ApiManifestTool? now)
    {
        if (old is null && now is null) return;
        if (old is null)
        {
            var changesData = !now!.Hints.Contains("read-only");
            Add(changesData ? ApiChangeKind.Security : ApiChangeKind.Compatible, changesData
                ? $"now the MCP tool '{now.Name}', which changes data: agents can call it"
                : $"now the MCP tool '{now.Name}'");
            return;
        }

        if (now is null)
        {
            Add(ApiChangeKind.Breaking, $"no longer an MCP tool (was '{old.Name}'): agents that use it stop working");
            return;
        }

        if (old.Name != now.Name)
            Add(ApiChangeKind.Breaking, $"MCP tool renamed from '{old.Name}' to '{now.Name}': agents that use it stop working");

        var tool = $"MCP tool '{now.Name}'";
        var before = old.Hints;
        var after = now.Hints;
        bool Had(string hint) => before.Contains(hint);
        bool Has(string hint) => after.Contains(hint);
        var writes = !Has("read-only");

        if (writes && Had("destructive") && !Has("destructive"))
            Add(ApiChangeKind.Security, $"{tool} is no longer marked destructive: agents may call it without asking the user");
        if (!Had("destructive") && Has("destructive"))
            Add(ApiChangeKind.Compatible, $"{tool} is now marked destructive");
        if (writes && Had("idempotent") && !Has("idempotent"))
            Add(ApiChangeKind.Breaking, $"{tool} is no longer idempotent: agents must not retry it");
        if (!Had("idempotent") && Has("idempotent"))
            Add(ApiChangeKind.Compatible, $"{tool} is now idempotent");
        if (Had("open-world") != Has("open-world"))
            Add(ApiChangeKind.Compatible, Has("open-world") ? $"{tool} now reaches beyond this API (open world)" : $"{tool} no longer reaches beyond this API");
        if (old.Description != now.Description)
            Add(ApiChangeKind.Compatible, $"{tool} description changed to \"{now.Description}\"");
    }

    private void CompareAuthorization(ApiManifestEndpoint old, ApiManifestEndpoint now)
    {
        var wasRequired = old.Authorization == "required";
        var isRequired = now.Authorization == "required";

        if (!wasRequired && isRequired)
        {
            Add(ApiChangeKind.Breaking, "now requires authorization");
            return;
        }

        if (wasRequired && !isRequired)
        {
            Add(ApiChangeKind.Security, $"no longer requires authorization (now {now.Authorization})");
            return;
        }

        if (!wasRequired)
        {
            if (old.Authorization != now.Authorization)
                Add(ApiChangeKind.Compatible, $"authorization changed from {old.Authorization} to {now.Authorization}");
            return;
        }

        CompareSet("policy", old.Policies, now.Policies, addedKind: ApiChangeKind.Breaking, removedKind: ApiChangeKind.Security);
        CompareSet("role", old.Roles, now.Roles, addedKind: ApiChangeKind.Breaking, removedKind: ApiChangeKind.Security);
        // Schemes are alternatives: removing one locks out its callers, adding one opens a new way in.
        CompareSet("authentication scheme", old.Schemes, now.Schemes, addedKind: ApiChangeKind.Security, removedKind: ApiChangeKind.Breaking);
    }

    private void CompareSet(string what, IReadOnlyList<string> old, IReadOnlyList<string> now, ApiChangeKind addedKind, ApiChangeKind removedKind)
    {
        foreach (var added in now.Except(old, StringComparer.Ordinal))
            Add(addedKind, addedKind == ApiChangeKind.Breaking ? $"now requires {what} '{added}'" : $"now accepts {what} '{added}'");
        foreach (var removed in old.Except(now, StringComparer.Ordinal))
            Add(removedKind, removedKind == ApiChangeKind.Breaking ? $"no longer accepts {what} '{removed}'" : $"no longer requires {what} '{removed}'");
    }

    private void CompareParameters(string what, IReadOnlyDictionary<string, string> old, IReadOnlyDictionary<string, string> now)
    {
        var oldByName = old.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        var nowByName = now.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);

        foreach (var (name, value) in nowByName.Where(p => !oldByName.ContainsKey(p.Key)))
        {
            if (Spec.Parse(value).Required) Add(ApiChangeKind.Breaking, $"requires new {what} '{name}'");
            else Add(ApiChangeKind.Compatible, $"accepts new optional {what} '{name}'");
        }

        foreach (var name in oldByName.Keys.Where(n => !nowByName.ContainsKey(n)))
            Add(ApiChangeKind.Compatible, $"{what} '{name}' removed (now ignored)");

        foreach (var (name, value) in oldByName.Where(p => nowByName.ContainsKey(p.Key)))
            CompareSpec($"{what} '{name}'", Spec.Parse(value), Spec.Parse(nowByName[name]), request: true);
    }

    private void CompareBody(string? old, string? now)
    {
        if (old is null && now is null) return;
        if (old is null)
        {
            if (Spec.Parse(now!).Required) Add(ApiChangeKind.Breaking, "now requires a request body");
            else Add(ApiChangeKind.Compatible, "accepts an optional request body");
            return;
        }

        if (now is null)
        {
            Add(ApiChangeKind.Compatible, "no longer reads a request body");
            return;
        }

        CompareSpec("request body", Spec.Parse(old), Spec.Parse(now), request: true);
    }

    private void CompareResponses(IReadOnlyDictionary<string, string> old, IReadOnlyDictionary<string, string> now)
    {
        foreach (var (status, value) in old)
        {
            if (!now.TryGetValue(status, out var current))
            {
                if (status.StartsWith('2')) Add(ApiChangeKind.Breaking, $"no longer answers {status}");
                else Add(ApiChangeKind.Compatible, $"no longer documents {status}");
                continue;
            }

            var before = Spec.Parse(value);
            var after = Spec.Parse(current);
            if (before.Type is not null && after.Type is null)
            {
                Add(ApiChangeKind.Breaking, $"response {status} no longer has a body");
                continue;
            }

            if (before.Type is null && after.Type is not null)
            {
                Add(ApiChangeKind.Compatible, $"response {status} now has a body");
                continue;
            }

            CompareSpec($"response {status}", before, after, request: false);
        }

        foreach (var status in now.Keys.Where(s => !old.ContainsKey(s)))
            Add(ApiChangeKind.Compatible, $"may now answer {status}");
    }

    private void CompareSpec(string where, Spec old, Spec now, bool request)
    {
        if (request && !old.Required && now.Required) Add(ApiChangeKind.Breaking, $"{where} is now required");

        // Media types: requests may accept more of them, responses may not switch away from one.
        foreach (var media in old.Media.Except(now.Media, StringComparer.OrdinalIgnoreCase))
            Add(ApiChangeKind.Breaking, request ? $"{where} no longer accepts {media}" : $"{where} is no longer {media}");
        foreach (var media in now.Media.Except(old.Media, StringComparer.OrdinalIgnoreCase))
            Add(ApiChangeKind.Compatible, request ? $"{where} also accepts {media}" : $"{where} may now be {media}");

        if (old.Type is not null && now.Type is not null) CompareType(where, old.Type, now.Type, request);
    }

    private void CompareType(string where, TypeExpr old, TypeExpr now, bool request)
    {
        if (request && old.Nullable && !now.Nullable) Add(ApiChangeKind.Breaking, $"{where} no longer accepts null");
        if (!request && !old.Nullable && now.Nullable) Add(ApiChangeKind.Breaking, $"{where} may now be null");

        // "any" accepts or produces anything: a request may widen to it, a response may narrow from it.
        if (old.Name == "any" || now.Name == "any")
        {
            if (old.Name == now.Name) return;
            if ((request && now.Name == "any") || (!request && old.Name == "any")) return;
            Add(ApiChangeKind.Breaking, $"{where} changed from {old} to {now}");
            return;
        }

        if (old.Kind != now.Kind)
        {
            Add(ApiChangeKind.Breaking, $"{where} changed from {old} to {now}");
            return;
        }

        switch (old.Kind)
        {
            case TypeKind.Array:
                CompareType(where + "[]", old.Element!, now.Element!, request);
                return;
            case TypeKind.Map:
                CompareType(where + "{}", old.Element!, now.Element!, request);
                return;
            case TypeKind.Primitive:
                if (old.Name == now.Name) return;
                // A request may accept a wider number; a response may return a narrower one.
                if (request ? Widens(old.Name, now.Name) : Widens(now.Name, old.Name)) return;
                Add(ApiChangeKind.Breaking, $"{where} changed from {old.Name} to {now.Name}");
                return;
            default:
                CompareNamed(where, old.Name, now.Name, request);
                return;
        }
    }

    private static readonly string[] Numbers = ["int32", "int64", "number"];

    private static bool Widens(string from, string to)
    {
        var a = Array.IndexOf(Numbers, from);
        var b = Array.IndexOf(Numbers, to);
        return a >= 0 && b >= 0 && a <= b;
    }

    private void CompareNamed(string where, string oldName, string newName, bool request)
    {
        // The pairs on the current path only: stops recursive types, still reports a type at every place it is used.
        if (!_visited.Add((oldName, newName, request))) return;
        try
        {
            CompareNamedTypes(where, oldName, newName, request);
        }
        finally
        {
            _visited.Remove((oldName, newName, request));
        }
    }

    private void CompareNamedTypes(string where, string oldName, string newName, bool request)
    {

        baseline.Types.TryGetValue(oldName, out var old);
        current.Types.TryGetValue(newName, out var now);
        if (old is null || now is null)
        {
            if (oldName != newName) Add(ApiChangeKind.Breaking, $"{where} changed from {oldName} to {newName}");
            return;
        }

        if ((old.Values is null) != (now.Values is null))
        {
            Add(ApiChangeKind.Breaking, $"{where} changed from {oldName} to {newName}");
            return;
        }

        if (old.Values is { } oldValues && now.Values is { } newValues)
        {
            foreach (var value in oldValues.Except(newValues, StringComparer.Ordinal))
                Add(request ? ApiChangeKind.Breaking : ApiChangeKind.Compatible,
                    request ? $"{where} no longer accepts {value}" : $"{where} no longer returns {value}");
            foreach (var value in newValues.Except(oldValues, StringComparer.Ordinal))
                Add(ApiChangeKind.Compatible, request ? $"{where} also accepts {value}" : $"{where} may now be {value}");
            return;
        }

        var oldProperties = old.Properties ?? ApiManifestEndpoint.Empty;
        var newProperties = now.Properties ?? ApiManifestEndpoint.Empty;

        foreach (var (name, value) in newProperties.Where(p => !oldProperties.ContainsKey(p.Key)))
        {
            if (!request) Add(ApiChangeKind.Compatible, $"{where}.{name} added");
            else if (Spec.Parse(value).Required) Add(ApiChangeKind.Breaking, $"{where}.{name} added as required");
            else Add(ApiChangeKind.Compatible, $"{where}.{name} added (optional)");
        }

        foreach (var name in oldProperties.Keys.Where(n => !newProperties.ContainsKey(n)))
        {
            if (request) Add(ApiChangeKind.Compatible, $"{where}.{name} removed (now ignored)");
            else Add(ApiChangeKind.Breaking, $"{where}.{name} removed");
        }

        foreach (var (name, value) in oldProperties.Where(p => newProperties.ContainsKey(p.Key)))
        {
            var before = Spec.Parse(value);
            var after = Spec.Parse(newProperties[name]);
            if (request && !before.Required && after.Required) Add(ApiChangeKind.Breaking, $"{where}.{name} is now required");
            if (before.Type is not null && after.Type is not null) CompareType($"{where}.{name}", before.Type, after.Type, request);
        }
    }

    private void Add(ApiChangeKind kind, string description) => _changes.Add(new ApiChange(kind, _endpoint, description));
}

/// <summary>A parameter, property, body or response value: <c>type[ as media, media][ (required)]</c>.</summary>
internal sealed record Spec(TypeExpr? Type, IReadOnlyList<string> Media, bool Required)
{
    private const string RequiredSuffix = " (required)";

    public static Spec Parse(string value, bool required = false)
    {
        var text = value.Trim();
        if (text.EndsWith(RequiredSuffix, StringComparison.Ordinal))
        {
            required = true;
            text = text[..^RequiredSuffix.Length].TrimEnd();
        }

        // No "as" part means JSON. A response without a body but with a media type is written "as text/plain".
        IReadOnlyList<string> media = ["application/json"];
        string? list = null;
        if (text.StartsWith("as ", StringComparison.Ordinal))
        {
            list = text[3..];
            text = string.Empty;
        }
        else if (text.IndexOf(" as ", StringComparison.Ordinal) is var asIndex and >= 0)
        {
            list = text[(asIndex + 4)..];
            text = text[..asIndex].Trim();
        }

        if (list is not null) media = [.. list.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];

        return new Spec(text.Length == 0 ? null : TypeExpr.Parse(text), media, required);
    }
}

internal enum TypeKind { Primitive, Named, Array, Map }

/// <summary>A parsed type expression: <c>int32</c>, <c>Invoice?</c>, <c>array&lt;map&lt;string&gt;&gt;</c>.</summary>
internal sealed record TypeExpr(TypeKind Kind, string Name, TypeExpr? Element, bool Nullable)
{
    private static readonly HashSet<string> Primitives = new(StringComparer.Ordinal)
    {
        "string", "boolean", "int32", "int64", "number", "date-time", "date", "time", "duration",
        "guid", "uri", "binary", "file", "any", "problem", "validation-problem",
    };

    public static TypeExpr Parse(string text)
    {
        text = text.Trim();
        var nullable = text.EndsWith('?');
        if (nullable) text = text[..^1].TrimEnd();

        if (TryGeneric(text, "array", out var element)) return new TypeExpr(TypeKind.Array, "array", Parse(element), nullable);
        if (TryGeneric(text, "map", out element)) return new TypeExpr(TypeKind.Map, "map", Parse(element), nullable);
        return new TypeExpr(Primitives.Contains(text) ? TypeKind.Primitive : TypeKind.Named, text, null, nullable);
    }

    private static bool TryGeneric(string text, string name, out string element)
    {
        element = string.Empty;
        if (!text.StartsWith(name + "<", StringComparison.Ordinal) || !text.EndsWith('>')) return false;
        element = text[(name.Length + 1)..^1];
        return true;
    }

    public override string ToString() => Kind switch
    {
        TypeKind.Array => $"array<{Element}>",
        TypeKind.Map => $"map<{Element}>",
        _ => Name,
    } + (Nullable ? "?" : string.Empty);
}
