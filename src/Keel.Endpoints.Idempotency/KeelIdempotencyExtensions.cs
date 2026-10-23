using Keel.Endpoints.Idempotency.Internal;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Keel.Endpoints.Idempotency;

/// <summary>Registration and opt-in for idempotent endpoints.</summary>
public static class KeelIdempotencyExtensions
{
    /// <summary>
    /// Registers idempotency. Responses are stored in <c>IDistributedCache</c>: in memory unless you register
    /// another cache (Redis: <c>AddStackExchangeRedisCache</c>), or in your own <see cref="IIdempotencyStore"/>.
    /// </summary>
    public static IServiceCollection AddKeelIdempotency(this IServiceCollection services, Action<KeelIdempotencyOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddOptions<KeelIdempotencyOptions>();
        if (configure is not null) services.Configure(configure);
        services.AddDistributedMemoryCache();   // no-op when another IDistributedCache is registered
        services.TryAddSingleton<IIdempotencyStore, DistributedCacheIdempotencyStore>();
        return services;
    }

    /// <summary>
    /// Makes the <c>POST</c> and <c>PATCH</c> endpoints under this builder (a route group, or one endpoint) idempotent:
    /// a request with an <c>Idempotency-Key</c> header runs once, and retries with the same key get the same
    /// response back. Other methods are idempotent by definition and pass through. Requires <see cref="AddKeelIdempotency"/>.
    /// </summary>
    public static TBuilder WithIdempotency<TBuilder>(this TBuilder builder, Action<IdempotencyEndpointOptions>? configure = null)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        var options = new IdempotencyEndpointOptions();
        configure?.Invoke(options);

        builder.WithMetadata(new IdempotencyMetadata(options));
        builder.AddEndpointFilterFactory((factoryContext, next) => IdempotencyFilter.Create(factoryContext, next, options));

        // Documented in OpenAPI and in the API manifest: what a client can get back because of the key.
        builder.Add(endpoint =>
        {
            endpoint.Metadata.Add(new ProducesResponseTypeMetadata(StatusCodes.Status409Conflict, typeof(ProblemDetails), ["application/problem+json"]));
            endpoint.Metadata.Add(new ProducesResponseTypeMetadata(StatusCodes.Status422UnprocessableEntity, typeof(ProblemDetails), ["application/problem+json"]));
            if (options.RequireKey)
                endpoint.Metadata.Add(new ProducesResponseTypeMetadata(StatusCodes.Status400BadRequest, typeof(ProblemDetails), ["application/problem+json"]));
        });
        return builder;
    }
}

/// <summary>Marks an endpoint as idempotent, with its settings.</summary>
/// <param name="Options">The endpoint's idempotency settings.</param>
public sealed record IdempotencyMetadata(IdempotencyEndpointOptions Options);
