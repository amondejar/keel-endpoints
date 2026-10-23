namespace Keel.Endpoints.Idempotency;

/// <summary>
/// Where keys and stored responses live. The default keeps them in <see cref="Microsoft.Extensions.Caching.Distributed.IDistributedCache"/>
/// (in memory unless you register Redis or another cache). Implement it on a store with an atomic
/// "set if absent" for strict guarantees across instances.
/// </summary>
public interface IIdempotencyStore
{
    /// <summary>
    /// Returns the stored response when the key has completed; otherwise claims the key for this request, or reports
    /// that another request holds it. <paramref name="key"/> is already scoped to the endpoint and the caller.
    /// </summary>
    ValueTask<IdempotencyClaim> TryClaimAsync(string key, TimeSpan inFlightTimeout, CancellationToken cancellationToken);

    /// <summary>Stores the response of a claimed key and releases the claim.</summary>
    ValueTask CompleteAsync(string key, IdempotentResponse response, TimeSpan retention, CancellationToken cancellationToken);

    /// <summary>Releases a claimed key without storing anything (the request failed): the next retry runs the handler.</summary>
    ValueTask ReleaseAsync(string key, CancellationToken cancellationToken);
}

/// <summary>The outcome of <see cref="IIdempotencyStore.TryClaimAsync"/>.</summary>
public enum IdempotencyClaimStatus
{
    /// <summary>The key is now held by this request: run it.</summary>
    Claimed,

    /// <summary>Another request with this key is still running.</summary>
    InFlight,

    /// <summary>A request with this key completed; <see cref="IdempotencyClaim.Response"/> holds its response.</summary>
    Completed,
}

/// <summary>The result of claiming a key.</summary>
/// <param name="Status">What happened.</param>
/// <param name="Response">The stored response, when <paramref name="Status"/> is <see cref="IdempotencyClaimStatus.Completed"/>.</param>
public sealed record IdempotencyClaim(IdempotencyClaimStatus Status, IdempotentResponse? Response = null);

/// <summary>A response kept for retries.</summary>
public sealed class IdempotentResponse
{
    /// <summary>A hash of the request the response answered; a retry must match it.</summary>
    public required string Fingerprint { get; init; }

    /// <summary>The HTTP status code.</summary>
    public required int StatusCode { get; init; }

    /// <summary>The response headers worth replaying (Content-Type, Location, ETag...).</summary>
    public IReadOnlyList<KeyValuePair<string, string>> Headers { get; init; } = [];

    /// <summary>The response body.</summary>
    public ReadOnlyMemory<byte> Body { get; init; }
}
