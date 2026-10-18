using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;

namespace Keel.Endpoints.Testing;

/// <summary>
/// Calls endpoint classes by type. The method, the route template and the binding come from the
/// endpoint, so a test cannot drift from the route it is testing:
/// <code>
/// var client = new EndpointClient(factory.CreateClient(), prefix: "/api");
/// var response = await client.SendAsync&lt;Invoices.Get.Endpoint, InvoiceView&gt;(new GetInvoice("42"));
/// </code>
/// Works with any <see cref="HttpClient"/>: one from <c>WebApplicationFactory</c>, a <c>TestServer</c>,
/// or a real host.
/// </summary>
public sealed partial class EndpointClient(HttpClient http, string prefix = "", JsonSerializerOptions? json = null)
{
    private readonly JsonSerializerOptions _json = json ?? new JsonSerializerOptions(JsonSerializerDefaults.Web);

    /// <summary>The underlying client, for headers and anything not covered here.</summary>
    public HttpClient Http { get; } = http;

    /// <summary>Sends <paramref name="request"/> to <typeparamref name="TEndpoint"/>.</summary>
    public Task<HttpResponseMessage> SendAsync<TEndpoint>(object? request = null, CancellationToken cancellationToken = default)
        where TEndpoint : EndpointBase
        => Http.SendAsync(CreateRequest(typeof(TEndpoint), request), cancellationToken);

    /// <summary>Sends <paramref name="request"/> and reads the JSON body as <typeparamref name="TResponse"/>
    /// when the call succeeds.</summary>
    public async Task<EndpointResponse<TResponse>> SendAsync<TEndpoint, TResponse>(object? request = null, CancellationToken cancellationToken = default)
        where TEndpoint : EndpointBase
    {
        using var response = await SendAsync<TEndpoint>(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var value = response.IsSuccessStatusCode && body.Length > 0
            ? JsonSerializer.Deserialize<TResponse>(body, _json)
            : default;
        return new EndpointResponse<TResponse>(response.StatusCode, value, body);
    }

    /// <summary>Builds the <see cref="HttpRequestMessage"/> for an endpoint without sending it.</summary>
    public HttpRequestMessage CreateRequest(Type endpointType, object? request)
    {
        var route = endpointType.GetCustomAttribute<EndpointRouteAttribute>(inherit: false)
            ?? throw new InvalidOperationException($"{endpointType.FullName} declares no route attribute.");

        var values = Flatten(request);
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var path = RouteParameter().Replace(route.Template, match =>
        {
            var name = match.Groups["name"].Value;
            if (!values.TryGetValue(name, out var value))
                throw new InvalidOperationException($"Route parameter '{name}' of {endpointType.Name} has no value on the request.");
            used.Add(name);
            return Uri.EscapeDataString(Format(value));
        });

        var url = Combine(prefix, path);
        var message = new HttpRequestMessage(new HttpMethod(route.Method), url);

        if (request is null) return message;

        if (UsesBody(route.Method, request.GetType()))
        {
            message.Content = JsonContent.Create(request, request.GetType(), options: _json);
        }
        else
        {
            // Members marked [FromBody] or [FromHeader] go where ASP.NET Core will look for them.
            foreach (var property in request.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                var attributes = property.GetCustomAttributes(inherit: true);
                if (attributes.OfType<IFromBodyMetadata>().Any())
                {
                    message.Content = JsonContent.Create(property.GetValue(request), property.PropertyType, options: _json);
                    used.Add(property.Name);
                }
                else if (attributes.OfType<IFromHeaderMetadata>().FirstOrDefault() is { } header)
                {
                    if (property.GetValue(request) is { } headerValue)
                        message.Headers.TryAddWithoutValidation(header.Name ?? property.Name, Format(headerValue));
                    used.Add(property.Name);
                }
            }

            var query = values.Where(kv => !used.Contains(kv.Key) && kv.Value is not null)
                .SelectMany(kv => Expand(kv.Key, kv.Value!))
                .ToList();
            if (query.Count > 0)
                message.RequestUri = new Uri(url + "?" + string.Join("&", query), UriKind.Relative);
        }

        return message;
    }

    private static bool UsesBody(string method, Type requestType)
    {
        if (requestType.GetCustomAttribute<BindRequestAttribute>() is { } bind) return bind.Binding == RequestBinding.Body;
        return HttpMethods.IsPost(method) || HttpMethods.IsPut(method) || HttpMethods.IsPatch(method);
    }

    private static Dictionary<string, object?> Flatten(object? request)
    {
        var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        if (request is null) return values;
        foreach (var property in request.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.GetIndexParameters().Length == 0) values[property.Name] = property.GetValue(request);
        }

        return values;
    }

    private static IEnumerable<string> Expand(string name, object value)
    {
        var key = Uri.EscapeDataString(char.ToLowerInvariant(name[0]) + name[1..]);
        if (value is System.Collections.IEnumerable items and not string)
        {
            foreach (var item in items) yield return key + "=" + Uri.EscapeDataString(Format(item));
        }
        else
        {
            yield return key + "=" + Uri.EscapeDataString(Format(value));
        }
    }

    private static string Format(object? value) => value switch
    {
        null => string.Empty,
        DateTimeOffset d => d.ToString("O", CultureInfo.InvariantCulture),
        DateTime d => d.ToString("O", CultureInfo.InvariantCulture),
        bool b => b ? "true" : "false",
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty,
    };

    private static string Combine(string prefix, string path)
    {
        var builder = new StringBuilder();
        builder.Append('/').Append(prefix.Trim('/'));
        if (builder.Length > 1) builder.Append('/');
        builder.Append(path.TrimStart('/'));
        return builder.ToString();
    }

    [GeneratedRegex(@"\{\*{0,2}(?<name>[A-Za-z_][A-Za-z0-9_]*)[^}]*\}")]
    private static partial Regex RouteParameter();
}

/// <summary>The outcome of a typed endpoint call.</summary>
/// <param name="StatusCode">The HTTP status code.</param>
/// <param name="Value">The deserialized body, when the call succeeded and returned one.</param>
/// <param name="Body">The raw body, useful for asserting on problem details.</param>
public sealed record EndpointResponse<T>(HttpStatusCode StatusCode, T? Value, string Body);
