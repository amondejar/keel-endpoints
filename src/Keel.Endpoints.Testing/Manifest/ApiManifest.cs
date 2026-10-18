using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Keel.Endpoints.Testing;

/// <summary>
/// The HTTP surface of an application, as clients see it: every endpoint's method and route, its
/// authorization, its parameters and body, its responses, and the JSON shape of every type involved.
/// Committed next to the tests as <c>api.manifest.json</c>, it turns API changes into a reviewable diff
/// and lets CI reject breaking ones. See <see cref="Verify"/>.
/// </summary>
/// <remarks>
/// Built from ApiExplorer, the same descriptions the OpenAPI document is built from, and from the
/// application's JSON options, so names and nullability are the ones on the wire.
/// </remarks>
public sealed class ApiManifest
{
    /// <summary>The manifest file format this version reads and writes.</summary>
    public const int CurrentFormat = 1;

    /// <summary>The file format version.</summary>
    public int Format { get; init; } = CurrentFormat;

    /// <summary>Endpoints keyed by method and route, for example <c>GET api/invoices/{invoiceId}</c>.</summary>
    public IReadOnlyDictionary<string, ApiManifestEndpoint> Endpoints { get; init; } = new SortedDictionary<string, ApiManifestEndpoint>(StringComparer.Ordinal);

    /// <summary>Named types referenced from endpoints and from other types, keyed by name.</summary>
    public IReadOnlyDictionary<string, ApiManifestType> Types { get; init; } = new SortedDictionary<string, ApiManifestType>(StringComparer.Ordinal);

    /// <summary>
    /// Describes the application's endpoints. Pass the services of a built application (a started
    /// <c>WebApplication</c>, or <c>WebApplicationFactory.Services</c>).
    /// </summary>
    public static ApiManifest Create(IServiceProvider services, ApiManifestOptions? options = null) =>
        ManifestBuilder.Build(services, options);

    /// <summary>Compares two manifests and lists every change, breaking ones first.</summary>
    public static IReadOnlyList<ApiChange> Compare(ApiManifest baseline, ApiManifest current) =>
        new ManifestComparer(baseline, current).Run();

    /// <summary>Reads a manifest from JSON.</summary>
    public static ApiManifest Parse(string json) => ManifestJson.Read(json);

    /// <summary>Reads a manifest file.</summary>
    public static ApiManifest Load(string path) => Parse(File.ReadAllText(path));

    /// <summary>The manifest as indented JSON with sorted keys and <c>\n</c> line endings, stable for diffs.</summary>
    public string ToJson() => ManifestJson.Write(this);

    /// <summary>
    /// Checks the application against the committed manifest at <paramref name="path"/> (relative to the
    /// calling source file's folder) and throws <see cref="ApiManifestException"/> listing the changes when
    /// they differ. A missing file is created, and the check fails so the new file gets reviewed.
    /// </summary>
    /// <remarks>
    /// To accept intended changes, run the tests with the environment variable <c>KEEL_UPDATE_MANIFEST</c>:
    /// <c>1</c> rewrites the file when every change is compatible; <c>all</c> also accepts breaking and
    /// security changes. CI does not set it, so any difference fails the build.
    /// </remarks>
    public static void Verify(
        IServiceProvider services,
        string path = "api.manifest.json",
        ApiManifestOptions? options = null,
        [CallerFilePath] string callerFilePath = "")
    {
        var file = ManifestFiles.Resolve(path, callerFilePath);
        var update = options?.Update ?? ManifestFiles.UpdateFromEnvironment();
        var current = Create(services, options);
        var json = current.ToJson();

        if (!File.Exists(file))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, json);
            if (update != ApiManifestUpdate.None) return;
            throw new ApiManifestException(
                $"API manifest created at {file}. Review it and commit it; the next run compares the API against it.", []);
        }

        var changes = Compare(Load(file), current);
        var accepted = update switch
        {
            ApiManifestUpdate.All => true,
            ApiManifestUpdate.Compatible => changes.All(c => c.Kind == ApiChangeKind.Compatible),
            _ => changes.Count == 0,
        };

        if (accepted)
        {
            if (update != ApiManifestUpdate.None && File.ReadAllText(file) != json) File.WriteAllText(file, json);
            return;
        }

        throw new ApiManifestException(Report(file, changes, update), changes);
    }

    private static string Report(string file, IReadOnlyList<ApiChange> changes, ApiManifestUpdate update)
    {
        int Count(ApiChangeKind kind) => changes.Count(c => c.Kind == kind);
        var text = new StringBuilder()
            .Append($"The API changed and {file} is out of date: ")
            .Append($"{Count(ApiChangeKind.Breaking)} breaking, {Count(ApiChangeKind.Security)} security, {Count(ApiChangeKind.Compatible)} compatible.\n\n");

        foreach (var change in changes) text.Append("  ").Append(change).Append('\n');

        text.Append('\n');
        if (update == ApiManifestUpdate.Compatible)
            text.Append("KEEL_UPDATE_MANIFEST=1 accepts compatible changes only. If the breaking or security changes are intended, run with KEEL_UPDATE_MANIFEST=all.\n");
        else
            text.Append("If the changes are intended, run the tests with KEEL_UPDATE_MANIFEST=1 (compatible changes only) or KEEL_UPDATE_MANIFEST=all, then commit the file.\n");
        return text.ToString();
    }
}

/// <summary>One endpoint in an <see cref="ApiManifest"/>.</summary>
/// <remarks>
/// Parameters, properties and the body are written as <c>type</c> or <c>type (required)</c>; the body and
/// responses add <c>as media/type</c> when they are not JSON. Types are <c>string</c>, <c>boolean</c>,
/// <c>int32</c>, <c>int64</c>, <c>number</c>, <c>date-time</c>, <c>date</c>, <c>time</c>, <c>duration</c>,
/// <c>guid</c>, <c>uri</c>, <c>binary</c>, <c>file</c>, <c>any</c>, <c>problem</c>,
/// <c>validation-problem</c>, <c>array&lt;T&gt;</c>, <c>map&lt;T&gt;</c> or a name from
/// <see cref="ApiManifest.Types"/>; a trailing <c>?</c> means the value can be null.
/// </remarks>
public sealed class ApiManifestEndpoint
{
    /// <summary>The HTTP method, upper case.</summary>
    public required string Method { get; init; }

    /// <summary>The route as ApiExplorer describes it, without a leading slash.</summary>
    public required string Route { get; init; }

    /// <summary>The endpoint name (the OpenAPI operation id), if any.</summary>
    public string? Operation { get; init; }

    /// <summary><c>anonymous</c>, <c>required</c>, or <c>none</c> when no decision was made.</summary>
    public string Authorization { get; init; } = "none";

    /// <summary>Authorization policies the caller must satisfy. <c>(inline)</c> stands for a policy object.</summary>
    public IReadOnlyList<string> Policies { get; init; } = [];

    /// <summary>Roles from <c>[Authorize(Roles = ...)]</c>.</summary>
    public IReadOnlyList<string> Roles { get; init; } = [];

    /// <summary>Authentication schemes from <c>[Authorize(AuthenticationSchemes = ...)]</c>.</summary>
    public IReadOnlyList<string> Schemes { get; init; } = [];

    /// <summary>Route parameters, in route order.</summary>
    public IReadOnlyList<KeyValuePair<string, string>> Path { get; init; } = [];

    /// <summary>Query string parameters.</summary>
    public IReadOnlyDictionary<string, string> Query { get; init; } = Empty;

    /// <summary>Header parameters.</summary>
    public IReadOnlyDictionary<string, string> Header { get; init; } = Empty;

    /// <summary>Form fields and files.</summary>
    public IReadOnlyDictionary<string, string> Form { get; init; } = Empty;

    /// <summary>The request body, if the endpoint reads one.</summary>
    public string? Body { get; init; }

    /// <summary>Documented responses keyed by status code; the value is empty when there is no body.</summary>
    public IReadOnlyDictionary<string, string> Responses { get; init; } = Empty;

    /// <summary>The manifest key: method and route.</summary>
    public override string ToString() => $"{Method} {Route}";

    internal static readonly IReadOnlyDictionary<string, string> Empty = new SortedDictionary<string, string>(StringComparer.Ordinal);
}

/// <summary>A named type in an <see cref="ApiManifest"/>: an object with properties, or an enum.</summary>
public sealed class ApiManifestType
{
    /// <summary>JSON properties of an object type, keyed by their JSON name.</summary>
    public IReadOnlyDictionary<string, string>? Properties { get; init; }

    /// <summary>The values of an enum type.</summary>
    public IReadOnlyList<string>? Values { get; init; }
}

/// <summary>Options for <see cref="ApiManifest.Create"/> and <see cref="ApiManifest.Verify"/>.</summary>
public sealed class ApiManifestOptions
{
    /// <summary>
    /// Which ApiExplorer descriptions to include; all by default. For example
    /// <c>d =&gt; d.RelativePath?.StartsWith("api/") == true</c>.
    /// </summary>
    public Func<Microsoft.AspNetCore.Mvc.ApiExplorer.ApiDescription, bool>? Include { get; set; }

    /// <summary>Overrides the <c>KEEL_UPDATE_MANIFEST</c> environment variable.</summary>
    public ApiManifestUpdate? Update { get; set; }
}

/// <summary>What <see cref="ApiManifest.Verify"/> may write to the manifest file.</summary>
public enum ApiManifestUpdate
{
    /// <summary>Only compare; never change the file (except creating a missing one).</summary>
    None,

    /// <summary>Rewrite the file when every change is compatible.</summary>
    Compatible,

    /// <summary>Rewrite the file whatever changed.</summary>
    All,
}

/// <summary>How a change affects existing clients.</summary>
public enum ApiChangeKind
{
    /// <summary>An existing client can stop working: something it sends is rejected, or something it reads is gone or different.</summary>
    Breaking,

    /// <summary>Clients keep working, but access got wider: less authorization than before.</summary>
    Security,

    /// <summary>Existing clients keep working.</summary>
    Compatible,
}

/// <summary>One difference between two manifests.</summary>
/// <param name="Kind">How the change affects existing clients.</param>
/// <param name="Endpoint">The endpoint, as its manifest key (<c>GET api/things/{id}</c>).</param>
/// <param name="Description">What changed, for example <c>response 200.amount removed</c>.</param>
public sealed record ApiChange(ApiChangeKind Kind, string Endpoint, string Description)
{
    /// <inheritdoc />
    public override string ToString() => $"{Kind.ToString().ToLowerInvariant(),-10} {Endpoint}: {Description}";
}

/// <summary>Thrown by <see cref="ApiManifest.Verify"/> when the API no longer matches its manifest.</summary>
public sealed class ApiManifestException(string message, IReadOnlyList<ApiChange> changes) : Exception(message)
{
    /// <summary>The changes found, breaking ones first. Empty when the manifest file was just created.</summary>
    public IReadOnlyList<ApiChange> Changes { get; } = changes;
}

internal static class ManifestFiles
{
    public static ApiManifestUpdate UpdateFromEnvironment() =>
        Environment.GetEnvironmentVariable("KEEL_UPDATE_MANIFEST")?.Trim().ToLowerInvariant() switch
        {
            "1" or "true" or "compatible" => ApiManifestUpdate.Compatible,
            "all" => ApiManifestUpdate.All,
            _ => ApiManifestUpdate.None,
        };

    /// <summary>
    /// Relative paths start at the calling source file's folder. When that folder does not exist (a build
    /// with mapped source paths, as in deterministic CI builds), they start at the test project's folder.
    /// </summary>
    public static string Resolve(string path, string callerFilePath)
    {
        if (Path.IsPathRooted(path)) return path;

        var callerDirectory = Path.GetDirectoryName(callerFilePath);
        if (!string.IsNullOrEmpty(callerDirectory) && Directory.Exists(callerDirectory))
            return Path.GetFullPath(Path.Combine(callerDirectory, path));

        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (directory.EnumerateFiles("*.csproj").Any()) return Path.GetFullPath(Path.Combine(directory.FullName, path));
        }

        return Path.GetFullPath(path);
    }
}

/// <summary>Reads and writes the manifest file. Written by hand so the output is stable and readable.</summary>
internal static class ManifestJson
{
    private static readonly JsonWriterOptions WriterOptions = new()
    {
        Indented = true,
        NewLine = "\n",
        // "array<Invoice>" stays readable instead of "array<Invoice>".
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string Write(ApiManifest manifest)
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream, WriterOptions))
        {
            w.WriteStartObject();
            w.WriteNumber("format", manifest.Format);

            w.WriteStartObject("endpoints");
            foreach (var (key, e) in manifest.Endpoints.OrderBy(e => e.Key, StringComparer.Ordinal))
            {
                w.WriteStartObject(key);
                if (e.Operation is not null) w.WriteString("operation", e.Operation);
                w.WriteString("authorization", e.Authorization);
                WriteList(w, "policies", e.Policies);
                WriteList(w, "roles", e.Roles);
                WriteList(w, "schemes", e.Schemes);
                WriteMap(w, "path", e.Path);
                WriteMap(w, "query", Sorted(e.Query));
                WriteMap(w, "header", Sorted(e.Header));
                WriteMap(w, "form", Sorted(e.Form));
                if (e.Body is not null) w.WriteString("body", e.Body);
                WriteMap(w, "responses", Sorted(e.Responses));
                w.WriteEndObject();
            }
            w.WriteEndObject();

            w.WriteStartObject("types");
            foreach (var (name, type) in manifest.Types.OrderBy(t => t.Key, StringComparer.Ordinal))
            {
                if (type.Values is { } values)
                {
                    w.WriteStartArray(name);
                    foreach (var value in values) w.WriteStringValue(value);
                    w.WriteEndArray();
                }
                else
                {
                    w.WriteStartObject(name);
                    foreach (var (property, value) in Sorted(type.Properties ?? ApiManifestEndpoint.Empty)) w.WriteString(property, value);
                    w.WriteEndObject();
                }
            }
            w.WriteEndObject();

            w.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray()) + "\n";
    }

    public static ApiManifest Read(string json)
    {
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
        var root = document.RootElement;
        var format = root.TryGetProperty("format", out var f) ? f.GetInt32() : CurrentFormatOrThrow();
        if (format > ApiManifest.CurrentFormat)
            throw new InvalidOperationException($"The API manifest has format {format}; this version of Keel.Endpoints.Testing reads up to {ApiManifest.CurrentFormat}.");

        var endpoints = new SortedDictionary<string, ApiManifestEndpoint>(StringComparer.Ordinal);
        if (root.TryGetProperty("endpoints", out var es))
        {
            foreach (var e in es.EnumerateObject())
            {
                var space = e.Name.IndexOf(' ');
                endpoints[e.Name] = new ApiManifestEndpoint
                {
                    Method = space < 0 ? e.Name : e.Name[..space],
                    Route = space < 0 ? string.Empty : e.Name[(space + 1)..],
                    Operation = e.Value.TryGetProperty("operation", out var op) ? op.GetString() : null,
                    Authorization = e.Value.TryGetProperty("authorization", out var auth) ? auth.GetString() ?? "none" : "none",
                    Policies = ReadList(e.Value, "policies"),
                    Roles = ReadList(e.Value, "roles"),
                    Schemes = ReadList(e.Value, "schemes"),
                    Path = ReadPairs(e.Value, "path"),
                    Query = ReadMap(e.Value, "query"),
                    Header = ReadMap(e.Value, "header"),
                    Form = ReadMap(e.Value, "form"),
                    Body = e.Value.TryGetProperty("body", out var body) ? body.GetString() : null,
                    Responses = ReadMap(e.Value, "responses"),
                };
            }
        }

        var types = new SortedDictionary<string, ApiManifestType>(StringComparer.Ordinal);
        if (root.TryGetProperty("types", out var ts))
        {
            foreach (var t in ts.EnumerateObject())
            {
                types[t.Name] = t.Value.ValueKind == JsonValueKind.Array
                    ? new ApiManifestType { Values = [.. t.Value.EnumerateArray().Select(v => v.GetString() ?? string.Empty)] }
                    : new ApiManifestType { Properties = ToMap(t.Value) };
            }
        }

        return new ApiManifest { Format = format, Endpoints = endpoints, Types = types };

        static int CurrentFormatOrThrow() => throw new InvalidOperationException("Not an API manifest: the \"format\" property is missing.");
    }

    private static IEnumerable<KeyValuePair<string, string>> Sorted(IReadOnlyDictionary<string, string> map) =>
        map.OrderBy(p => p.Key, StringComparer.Ordinal);

    private static void WriteList(Utf8JsonWriter w, string name, IReadOnlyList<string> values)
    {
        if (values.Count == 0) return;
        w.WriteStartArray(name);
        foreach (var value in values) w.WriteStringValue(value);
        w.WriteEndArray();
    }

    private static void WriteMap(Utf8JsonWriter w, string name, IEnumerable<KeyValuePair<string, string>> values)
    {
        var list = values.ToList();
        if (list.Count == 0) return;
        w.WriteStartObject(name);
        foreach (var (key, value) in list) w.WriteString(key, value);
        w.WriteEndObject();
    }

    private static IReadOnlyList<string> ReadList(JsonElement e, string name) =>
        e.TryGetProperty(name, out var list) ? [.. list.EnumerateArray().Select(v => v.GetString() ?? string.Empty)] : [];

    private static IReadOnlyList<KeyValuePair<string, string>> ReadPairs(JsonElement e, string name) =>
        e.TryGetProperty(name, out var map) ? [.. map.EnumerateObject().Select(p => KeyValuePair.Create(p.Name, p.Value.GetString() ?? string.Empty))] : [];

    private static IReadOnlyDictionary<string, string> ReadMap(JsonElement e, string name) =>
        e.TryGetProperty(name, out var map) ? ToMap(map) : ApiManifestEndpoint.Empty;

    private static SortedDictionary<string, string> ToMap(JsonElement map)
    {
        var result = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var p in map.EnumerateObject()) result[p.Name] = p.Value.GetString() ?? string.Empty;
        return result;
    }
}
