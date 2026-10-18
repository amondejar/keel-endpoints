using System.ComponentModel.DataAnnotations;
using System.IO.Pipelines;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using HttpJsonOptions = Microsoft.AspNetCore.Http.Json.JsonOptions;

namespace Keel.Endpoints.Testing;

/// <summary>Builds an <see cref="ApiManifest"/> from ApiExplorer and the application's JSON options.</summary>
internal static class ManifestBuilder
{
    private const string Json = "application/json";
    private const string ProblemJson = "application/problem+json";

    public static ApiManifest Build(IServiceProvider services, ApiManifestOptions? options)
    {
        var explorer = services.GetService<IApiDescriptionGroupCollectionProvider>()
            ?? throw new InvalidOperationException(
                "The API manifest is built from ApiExplorer, which is not registered. AddKeelEndpoints() registers it; " +
                "otherwise call builder.Services.AddEndpointsApiExplorer().");

        var json = new JsonSerializerOptions(services.GetService<IOptions<HttpJsonOptions>>()?.Value.SerializerOptions
            ?? new JsonSerializerOptions(JsonSerializerDefaults.Web));
        json.TypeInfoResolver ??= new DefaultJsonTypeInfoResolver();
        var types = new TypeDescriber(json);

        var endpoints = new SortedDictionary<string, ApiManifestEndpoint>(StringComparer.Ordinal);
        foreach (var description in explorer.ApiDescriptionGroups.Items.SelectMany(g => g.Items))
        {
            if (options?.Include is { } include && !include(description)) continue;

            var endpoint = Describe(description, types);
            endpoints.TryAdd(endpoint.ToString(), endpoint);
        }

        return new ApiManifest { Endpoints = endpoints, Types = types.Types };
    }

    private static ApiManifestEndpoint Describe(ApiDescription d, TypeDescriber types)
    {
        var route = (d.RelativePath ?? string.Empty).Trim('/');
        var metadata = d.ActionDescriptor.EndpointMetadata ?? [];
        var authorize = metadata.OfType<IAuthorizeData>().ToList();
        var requiresAuthorization = authorize.Count > 0
            || metadata.OfType<AuthorizationPolicy>().Any()
            || metadata.OfType<IAuthorizationRequirementData>().Any();

        string Parameter(ApiParameterDescription p) =>
            types.Describe(p.Type ?? typeof(string)) + (p.IsRequired ? " (required)" : string.Empty);

        Dictionary<string, string> Parameters(params string[] sources) =>
            d.ParameterDescriptions
                .Where(p => sources.Contains(p.Source.Id))
                .GroupBy(p => p.Name, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => Parameter(g.First()), StringComparer.Ordinal);

        var body = d.ParameterDescriptions.FirstOrDefault(p => p.Source.Id == "Body");
        var requestMedia = d.SupportedRequestFormats.Select(f => f.MediaType);

        return new ApiManifestEndpoint
        {
            Method = (d.HttpMethod ?? "ANY").ToUpperInvariant(),
            Route = route,
            Operation = metadata.OfType<IEndpointNameMetadata>().LastOrDefault()?.EndpointName,
            Authorization = metadata.OfType<IAllowAnonymous>().Any() ? "anonymous" : requiresAuthorization ? "required" : "none",
            Policies = Distinct(authorize.Select(a => a.Policy)
                .Concat(metadata.OfType<AuthorizationPolicy>().Select(_ => "(inline)"))),
            Roles = Distinct(authorize.SelectMany(a => Split(a.Roles))),
            Schemes = Distinct(authorize.SelectMany(a => Split(a.AuthenticationSchemes))),
            // Route parameters in the order they appear in the route: their names are invisible to clients.
            Path = [.. d.ParameterDescriptions
                .Where(p => p.Source.Id == "Path")
                .GroupBy(p => p.Name, StringComparer.Ordinal).Select(g => g.First())
                .OrderBy(p => IndexInRoute(route, p.Name))
                .Select(p => KeyValuePair.Create(p.Name, types.Describe(p.Type ?? typeof(string))))],
            Query = Parameters("Query"),
            Header = Parameters("Header"),
            Form = Parameters("Form", "FormFile"),
            Body = body is null
                ? null
                : types.Describe(body.Type ?? typeof(object)) + Media(requestMedia, null) + (body.IsRequired ? " (required)" : string.Empty),
            Responses = d.SupportedResponseTypes
                .Where(r => !r.IsDefaultResponse)
                .GroupBy(r => r.StatusCode)
                .OrderBy(g => g.Key)
                .ToDictionary(
                    g => g.Key.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    g =>
                    {
                        var type = g.Select(r => r.Type).FirstOrDefault(t => t is not null && t != typeof(void));
                        var described = type is null ? string.Empty : types.Describe(type);
                        return (described + Media(g.SelectMany(r => r.ApiResponseFormats).Select(f => f.MediaType), type)).Trim();
                    },
                    StringComparer.Ordinal),
        };
    }

    /// <summary><c>" as text/plain"</c> when the media types are not just JSON; JSON is the default.</summary>
    private static string Media(IEnumerable<string?> mediaTypes, Type? type)
    {
        var other = mediaTypes
            .Where(m => !string.IsNullOrEmpty(m) && m != Json && !(m == ProblemJson && type is not null && typeof(ProblemDetails).IsAssignableFrom(type)))
            .Select(m => m!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.Ordinal)
            .ToList();
        return other.Count == 0 ? string.Empty : " as " + string.Join(", ", other);
    }

    private static int IndexInRoute(string route, string name)
    {
        var index = route.IndexOf("{" + name, StringComparison.OrdinalIgnoreCase);
        if (index < 0) index = route.IndexOf("{*" + name, StringComparison.OrdinalIgnoreCase);
        return index < 0 ? int.MaxValue : index;
    }

    private static IEnumerable<string> Split(string? list) =>
        (list ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static List<string> Distinct(IEnumerable<string?> values) =>
        [.. values.Where(v => !string.IsNullOrEmpty(v)).Select(v => v!).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
}

/// <summary>Turns CLR types into manifest type expressions, collecting named object and enum types.</summary>
internal sealed class TypeDescriber(JsonSerializerOptions json)
{
    private readonly Dictionary<Type, string> _names = [];

    public SortedDictionary<string, ApiManifestType> Types { get; } = new(StringComparer.Ordinal);

    public string Describe(Type type)
    {
        if (Nullable.GetUnderlyingType(type) is { } underlying) return Describe(underlying) + "?";
        if (Primitive(type) is { } primitive) return primitive;
        // A Server-Sent Event's data is what clients parse; the event envelope is the media type's business.
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(System.Net.ServerSentEvents.SseItem<>))
            return Describe(type.GetGenericArguments()[0]);
        if (type.IsEnum) return Named(type, () => new ApiManifestType { Values = Enum.GetNames(type) });

        JsonTypeInfo info;
        try { info = json.GetTypeInfo(type); }
        catch (Exception e) when (e is NotSupportedException or InvalidOperationException or ArgumentException) { return "any"; }

        return info.Kind switch
        {
            JsonTypeInfoKind.Enumerable => $"array<{Describe(info.ElementType ?? typeof(object))}>",
            JsonTypeInfoKind.Dictionary => $"map<{Describe(info.ElementType ?? typeof(object))}>",
            JsonTypeInfoKind.Object => Named(type, () => Object(info)),
            _ => "any",
        };
    }

    private ApiManifestType Object(JsonTypeInfo info)
    {
        var properties = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var p in info.Properties)
        {
            if (p.Get is null && p.Set is null) continue;

            var described = Describe(p.PropertyType);
            if (!p.PropertyType.IsValueType && (p.IsGetNullable || p.IsSetNullable) && !described.EndsWith('?')) described += "?";

            // Required on the wire: JSON-required, or [Required] without a constructor default to fall back on.
            var required = p.IsRequired
                || (p.AttributeProvider?.IsDefined(typeof(RequiredAttribute), inherit: true) == true && p.AssociatedParameter?.HasDefaultValue != true);
            properties[p.Name] = described + (required ? " (required)" : string.Empty);
        }

        return new ApiManifestType { Properties = properties };
    }

    /// <summary>Registers the name before describing the members, so recursive types terminate.</summary>
    private string Named(Type type, Func<ApiManifestType> describe)
    {
        if (_names.TryGetValue(type, out var known)) return known;

        var name = FriendlyName(type, fullName: false);
        if (Types.ContainsKey(name)) name = FriendlyName(type, fullName: true);
        _names[type] = name;
        Types[name] = new ApiManifestType { Properties = new SortedDictionary<string, string>(StringComparer.Ordinal) };
        Types[name] = describe();
        return name;
    }

    private static string FriendlyName(Type type, bool fullName)
    {
        var name = fullName ? (type.FullName ?? type.Name) : type.Name;
        if (!type.IsGenericType) return name.Replace('+', '.');
        var tick = name.IndexOf('`');
        var baseName = tick < 0 ? name : name[..tick];
        return $"{baseName.Replace('+', '.')}<{string.Join(",", type.GetGenericArguments().Select(a => FriendlyName(a, fullName)))}>";
    }

    private static string? Primitive(Type type)
    {
        if (type == typeof(string) || type == typeof(char)) return "string";
        if (type == typeof(bool)) return "boolean";
        if (type == typeof(byte) || type == typeof(sbyte) || type == typeof(short) || type == typeof(ushort) || type == typeof(int) || type == typeof(uint)) return "int32";
        if (type == typeof(long) || type == typeof(ulong)) return "int64";
        if (type == typeof(float) || type == typeof(double) || type == typeof(decimal) || type == typeof(Half)) return "number";
        if (type == typeof(DateTime) || type == typeof(DateTimeOffset)) return "date-time";
        if (type == typeof(DateOnly)) return "date";
        if (type == typeof(TimeOnly)) return "time";
        if (type == typeof(TimeSpan)) return "duration";
        if (type == typeof(Guid)) return "guid";
        if (type == typeof(Uri)) return "uri";
        if (type == typeof(byte[]) || typeof(Stream).IsAssignableFrom(type) || type == typeof(PipeReader)) return "binary";
        if (type == typeof(IFormFile)) return "file";
        if (type == typeof(IFormFileCollection)) return "array<file>";
        if (type == typeof(object) || type == typeof(JsonElement) || type == typeof(JsonDocument) || typeof(JsonNode).IsAssignableFrom(type)) return "any";
        if (typeof(HttpValidationProblemDetails).IsAssignableFrom(type)) return "validation-problem";
        if (typeof(ProblemDetails).IsAssignableFrom(type)) return "problem";
        return null;
    }
}
