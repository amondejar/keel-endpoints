using Keel.Endpoints.Idempotency;

namespace Keel.Endpoints;

/// <summary>Idempotency in an endpoint's or group's <c>Configure</c>.</summary>
public static class IdempotencyEndpointOptionsExtensions
{
    /// <summary>
    /// Makes the endpoint idempotent with an <c>Idempotency-Key</c> header: retries with the same key get the first
    /// response back without running the handler again. Requires <c>AddKeelIdempotency()</c>.
    /// </summary>
    public static EndpointOptions Idempotent(this EndpointOptions options, Action<IdempotencyEndpointOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        return options.Convention(b => b.WithIdempotency(configure));
    }
}
