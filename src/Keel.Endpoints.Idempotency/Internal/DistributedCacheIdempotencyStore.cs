using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;

namespace Keel.Endpoints.Idempotency.Internal;

/// <summary>
/// The default store, on <see cref="IDistributedCache"/>. Claims are exact within one instance; across instances
/// sharing a cache (Redis), two requests arriving in the same few milliseconds on different instances can both
/// run, because the cache has no atomic "set if absent". Implement <see cref="IIdempotencyStore"/> on such an
/// operation (Redis SET NX, a unique database row) when that matters.
/// </summary>
internal sealed class DistributedCacheIdempotencyStore(IDistributedCache cache) : IIdempotencyStore
{
    private const string Prefix = "keel:idempotency:";
    private static readonly byte[] Marker = [1];
    private readonly ConcurrentDictionary<string, byte> _inFlight = new(StringComparer.Ordinal);

    public async ValueTask<IdempotencyClaim> TryClaimAsync(string key, TimeSpan inFlightTimeout, CancellationToken cancellationToken)
    {
        if (await cache.GetAsync(Prefix + key, cancellationToken) is { } stored)
            return new IdempotencyClaim(IdempotencyClaimStatus.Completed, Read(stored));

        if (!_inFlight.TryAdd(key, 0)) return new IdempotencyClaim(IdempotencyClaimStatus.InFlight);

        try
        {
            // Another instance may hold it (shared cache).
            if (await cache.GetAsync(Prefix + key + ":lock", cancellationToken) is not null)
            {
                _inFlight.TryRemove(key, out _);
                return new IdempotencyClaim(IdempotencyClaimStatus.InFlight);
            }

            await cache.SetAsync(Prefix + key + ":lock", Marker,
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = inFlightTimeout }, cancellationToken);

            // It may have completed between the first read and the claim.
            if (await cache.GetAsync(Prefix + key, cancellationToken) is { } late)
            {
                await ReleaseAsync(key, cancellationToken);
                return new IdempotencyClaim(IdempotencyClaimStatus.Completed, Read(late));
            }

            return new IdempotencyClaim(IdempotencyClaimStatus.Claimed);
        }
        catch
        {
            _inFlight.TryRemove(key, out _);
            throw;
        }
    }

    public async ValueTask CompleteAsync(string key, IdempotentResponse response, TimeSpan retention, CancellationToken cancellationToken)
    {
        try
        {
            await cache.SetAsync(Prefix + key, Write(response),
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = retention }, cancellationToken);
            await cache.RemoveAsync(Prefix + key + ":lock", cancellationToken);
        }
        finally
        {
            _inFlight.TryRemove(key, out _);
        }
    }

    public async ValueTask ReleaseAsync(string key, CancellationToken cancellationToken)
    {
        try
        {
            await cache.RemoveAsync(Prefix + key + ":lock", cancellationToken);
        }
        finally
        {
            _inFlight.TryRemove(key, out _);
        }
    }

    // A small JSON document, written by hand so it needs no serializer context (Native AOT).
    internal static byte[] Write(IdempotentResponse response)
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream))
        {
            w.WriteStartObject();
            w.WriteNumber("v", 1);
            w.WriteString("fingerprint", response.Fingerprint);
            w.WriteNumber("status", response.StatusCode);
            w.WriteStartArray("headers");
            foreach (var (name, value) in response.Headers)
            {
                w.WriteStartArray();
                w.WriteStringValue(name);
                w.WriteStringValue(value);
                w.WriteEndArray();
            }

            w.WriteEndArray();
            w.WriteBase64String("body", response.Body.Span);
            w.WriteEndObject();
        }

        return stream.ToArray();
    }

    internal static IdempotentResponse Read(byte[] data)
    {
        using var document = JsonDocument.Parse(data);
        var root = document.RootElement;
        return new IdempotentResponse
        {
            Fingerprint = root.GetProperty("fingerprint").GetString() ?? string.Empty,
            StatusCode = root.GetProperty("status").GetInt32(),
            Headers = [.. root.GetProperty("headers").EnumerateArray()
                .Select(h => KeyValuePair.Create(h[0].GetString() ?? string.Empty, h[1].GetString() ?? string.Empty))],
            Body = root.GetProperty("body").GetBytesFromBase64(),
        };
    }
}
