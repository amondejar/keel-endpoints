using System.IO.Pipelines;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using HttpJsonOptions = Microsoft.AspNetCore.Http.Json.JsonOptions;

namespace Keel.Endpoints.Idempotency.Internal;

/// <summary>
/// The endpoint filter behind <c>WithIdempotency()</c>. It runs after authentication, binding and validation:
/// a request that fails those never touches a key, and the caller is known when the key is scoped.
/// </summary>
internal static class IdempotencyFilter
{
    public const string ReplayedHeader = "Idempotent-Replayed";

    // Not replayed: they describe this connection or this response's framing, or set state on the client.
    private static readonly HashSet<string> SkippedHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Set-Cookie", "Date", "Server", "Transfer-Encoding", "Content-Length", "Connection", "Keep-Alive",
    };

    public static EndpointFilterDelegate Create(EndpointFilterFactoryContext factoryContext, EndpointFilterDelegate next, IdempotencyEndpointOptions endpointOptions)
    {
        var services = factoryContext.ApplicationServices;
        var store = services.GetService<IIdempotencyStore>()
            ?? throw new InvalidOperationException("WithIdempotency() / options.Idempotent() needs builder.Services.AddKeelIdempotency().");
        var options = services.GetRequiredService<IOptions<KeelIdempotencyOptions>>().Value;
        var json = services.GetRequiredService<IOptions<HttpJsonOptions>>().Value.SerializerOptions;
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("Keel.Endpoints.Idempotency");
        var retention = endpointOptions.Retention ?? options.Retention;

        return async context =>
        {
            var http = context.HttpContext;
            if (!HttpMethods.IsPost(http.Request.Method) && !HttpMethods.IsPatch(http.Request.Method)) return await next(context);

            var key = http.Request.Headers[options.HeaderName].ToString();
            if (key.Length == 0)
            {
                return endpointOptions.RequireKey
                    ? Problem(StatusCodes.Status400BadRequest, $"The {options.HeaderName} header is required", $"Send a unique {options.HeaderName} (a UUID, say) with each new request, and the same one on retries.")
                    : await next(context);
            }

            if (key.Length > options.MaxKeyLength)
                return Problem(StatusCodes.Status400BadRequest, $"The {options.HeaderName} header is too long", $"Keys are at most {options.MaxKeyLength} characters.");

            var scoped = Scope(http, key);
            var fingerprint = Fingerprint(context.Arguments, json);
            var claim = await store.TryClaimAsync(scoped, options.InFlightTimeout, http.RequestAborted);

            switch (claim.Status)
            {
                case IdempotencyClaimStatus.Completed when claim.Response!.Fingerprint != fingerprint:
                    return Problem(StatusCodes.Status422UnprocessableEntity, $"The {options.HeaderName} was used for a different request",
                        "A key identifies one request; send a new key for a new request.");

                case IdempotencyClaimStatus.Completed:
                    await ReplayAsync(http, claim.Response!);
                    return Results.Empty;

                case IdempotencyClaimStatus.InFlight:
                    http.Response.Headers.RetryAfter = "1";
                    return Problem(StatusCodes.Status409Conflict, $"A request with this {options.HeaderName} is still being processed",
                        "Retry once it has completed to get its response.");
            }

            return await RunAndStoreAsync(context, next, store, scoped, fingerprint, retention, options.MaxResponseSize, json, logger);
        };
    }

    private static async ValueTask<object?> RunAndStoreAsync(
        EndpointFilterInvocationContext context, EndpointFilterDelegate next, IIdempotencyStore store, string key, string fingerprint,
        TimeSpan retention, int maxResponseSize, JsonSerializerOptions json, ILogger logger)
    {
        var http = context.HttpContext;
        var stored = false;
        try
        {
            var result = await next(context);
            if (IsStream(result)) return result; // a stream is not replayable: let it through, release the key

            // Write the response into a buffer, keep it, then send it.
            var original = http.Response.Body;
            using var buffer = new MemoryStream();
            http.Response.Body = buffer;
            try
            {
                await WriteAsync(http, result, json);
            }
            finally
            {
                http.Response.Body = original;
            }

            var status = http.Response.StatusCode;
            if (status < 500 && buffer.Length <= maxResponseSize)
            {
                var response = new IdempotentResponse
                {
                    Fingerprint = fingerprint,
                    StatusCode = status,
                    Headers = [.. http.Response.Headers
                        .Where(h => !SkippedHeaders.Contains(h.Key))
                        .SelectMany(h => h.Value.Select(v => KeyValuePair.Create(h.Key, v ?? string.Empty)))],
                    Body = buffer.ToArray(),
                };
                await store.CompleteAsync(key, response, retention, CancellationToken.None);
                stored = true;
            }
            else if (status < 500)
            {
                logger.LogWarning("Response of {Length} bytes exceeds KeelIdempotencyOptions.MaxResponseSize ({Max}); not stored, retries will run the handler again.",
                    buffer.Length, maxResponseSize);
            }

            http.Response.ContentLength = buffer.Length;
            buffer.Position = 0;
            await buffer.CopyToAsync(original, http.RequestAborted);
            return Results.Empty;
        }
        finally
        {
            // Failures (an exception, a 5xx, an unstored response) release the key so a retry runs again.
            if (!stored) await store.ReleaseAsync(key, CancellationToken.None);
        }
    }

    /// <summary>Writes what the handler returned, as ASP.NET Core would: an IResult executes, text is text, the rest is JSON.</summary>
    private static async Task WriteAsync(HttpContext http, object? result, JsonSerializerOptions json)
    {
        switch (result)
        {
            case IResult r:
                await r.ExecuteAsync(http);
                break;
            case null:
                break;
            case string s:
                http.Response.ContentType ??= "text/plain; charset=utf-8";
                await http.Response.WriteAsync(s);
                break;
            default:
                await http.Response.WriteAsJsonAsync(result, json.GetTypeInfo(result.GetType()));
                break;
        }
    }

    // Server-Sent Events, or a JSON stream of reference-type items (IAsyncEnumerable<T> is covariant).
    private static bool IsStream(object? result) =>
        result is IAsyncEnumerable<object>
        || result?.GetType().Name.StartsWith("ServerSentEventsResult", StringComparison.Ordinal) == true;

    private static async Task ReplayAsync(HttpContext http, IdempotentResponse response)
    {
        http.Response.StatusCode = response.StatusCode;
        foreach (var group in response.Headers.GroupBy(h => h.Key, StringComparer.OrdinalIgnoreCase))
            http.Response.Headers[group.Key] = group.Select(h => h.Value).ToArray();
        http.Response.Headers[ReplayedHeader] = "true";
        http.Response.ContentLength = response.Body.Length;
        await http.Response.Body.WriteAsync(response.Body, http.RequestAborted);
    }

    /// <summary>The key is scoped to the endpoint and the caller, so one caller can never get another's response.</summary>
    private static string Scope(HttpContext http, string key)
    {
        var endpoint = (http.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText ?? http.Request.Path.ToString();
        var user = http.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? http.User.Identity?.Name ?? string.Empty;
        var text = $"{http.Request.Method}\n{endpoint}\n{user}\n{key}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }

    /// <summary>
    /// A hash of the bound request (contract, route and query values): the same JSON with members in another order is
    /// the same request. Types the app's JSON options cannot serialize fall back to their text form.
    /// </summary>
    private static string Fingerprint(IList<object?> arguments, JsonSerializerOptions json)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var argument in arguments)
        {
            if (argument is IServiceProvider or CancellationToken or HttpContext or HttpRequest or HttpResponse
                or ClaimsPrincipal or Stream or PipeReader) continue;

            if (argument is null)
            {
                hash.AppendData("null;"u8);
                continue;
            }

            hash.AppendData(json.TryGetTypeInfo(argument.GetType(), out var info) && info.Kind != JsonTypeInfoKind.None || argument is string
                ? JsonSerializer.SerializeToUtf8Bytes(argument, json.GetTypeInfo(argument.GetType()))
                : Encoding.UTF8.GetBytes(argument.ToString() ?? string.Empty));
            hash.AppendData(";"u8);
        }

        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static IResult Problem(int status, string title, string detail) =>
        TypedResults.Problem(statusCode: status, title: title, detail: detail);
}
