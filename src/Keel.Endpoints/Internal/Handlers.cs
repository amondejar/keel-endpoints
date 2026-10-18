using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics.CodeAnalysis;

namespace Keel.Endpoints.Internal;

/// <summary>
/// The handlers handed to ASP.NET Core's RequestDelegateFactory, one per endpoint shape and
/// binding. Because they are ordinary Minimal API handlers, binding, validation, typed results
/// and OpenAPI metadata all come from ASP.NET Core itself.
/// </summary>
internal static class Handlers
{
    // Endpoint<TRequest, TResponse>
    public static Task<TResponse> ResponseFromParameters<[DynamicallyAccessedMembers(Ctors)] TEndpoint, TRequest, TResponse>(
        [AsParameters] TRequest request, [FromServices] IServiceProvider services, CancellationToken ct)
        where TEndpoint : Endpoint<TRequest, TResponse>
        where TRequest : notnull
        => Activate<TEndpoint>(services).HandleAsync(request, ct);

    public static Task<TResponse> ResponseFromBody<[DynamicallyAccessedMembers(Ctors)] TEndpoint, TRequest, TResponse>(
        [FromBody] TRequest request, [FromServices] IServiceProvider services, CancellationToken ct)
        where TEndpoint : Endpoint<TRequest, TResponse>
        where TRequest : notnull
        => Activate<TEndpoint>(services).HandleAsync(request, ct);

    // Endpoint<TRequest>
    public static async Task<NoContent> NoContentFromParameters<[DynamicallyAccessedMembers(Ctors)] TEndpoint, TRequest>(
        [AsParameters] TRequest request, [FromServices] IServiceProvider services, CancellationToken ct)
        where TEndpoint : Endpoint<TRequest>
        where TRequest : notnull
    {
        await Activate<TEndpoint>(services).HandleAsync(request, ct);
        return TypedResults.NoContent();
    }

    public static async Task<NoContent> NoContentFromBody<[DynamicallyAccessedMembers(Ctors)] TEndpoint, TRequest>(
        [FromBody] TRequest request, [FromServices] IServiceProvider services, CancellationToken ct)
        where TEndpoint : Endpoint<TRequest>
        where TRequest : notnull
    {
        await Activate<TEndpoint>(services).HandleAsync(request, ct);
        return TypedResults.NoContent();
    }

    // EndpointWithoutRequest<TResponse>
    public static Task<TResponse> WithoutRequest<[DynamicallyAccessedMembers(Ctors)] TEndpoint, TResponse>([FromServices] IServiceProvider services, CancellationToken ct)
        where TEndpoint : EndpointWithoutRequest<TResponse>
        => Activate<TEndpoint>(services).HandleAsync(ct);

    // ResultEndpoint<TRequest, TResult>
    public static Task<TResult> ResultFromParameters<[DynamicallyAccessedMembers(Ctors)] TEndpoint, TRequest, TResult>(
        [AsParameters] TRequest request, [FromServices] IServiceProvider services, CancellationToken ct)
        where TEndpoint : ResultEndpoint<TRequest, TResult>
        where TRequest : notnull
        where TResult : IResult
        => Activate<TEndpoint>(services).HandleAsync(request, ct);

    public static Task<TResult> ResultFromBody<[DynamicallyAccessedMembers(Ctors)] TEndpoint, TRequest, TResult>(
        [FromBody] TRequest request, [FromServices] IServiceProvider services, CancellationToken ct)
        where TEndpoint : ResultEndpoint<TRequest, TResult>
        where TRequest : notnull
        where TResult : IResult
        => Activate<TEndpoint>(services).HandleAsync(request, ct);

    // StreamEndpoint<TRequest, TItem>
    public static IAsyncEnumerable<TItem> JsonStreamFromParameters<[DynamicallyAccessedMembers(Ctors)] TEndpoint, TRequest, TItem>(
        [AsParameters] TRequest request, [FromServices] IServiceProvider services, CancellationToken ct)
        where TEndpoint : StreamEndpoint<TRequest, TItem>
        where TRequest : notnull
        => Activate<TEndpoint>(services).HandleAsync(request, ct);

    public static IAsyncEnumerable<TItem> JsonStreamFromBody<[DynamicallyAccessedMembers(Ctors)] TEndpoint, TRequest, TItem>(
        [FromBody] TRequest request, [FromServices] IServiceProvider services, CancellationToken ct)
        where TEndpoint : StreamEndpoint<TRequest, TItem>
        where TRequest : notnull
        => Activate<TEndpoint>(services).HandleAsync(request, ct);

    public static ServerSentEventsResult<TItem> EventStreamFromParameters<[DynamicallyAccessedMembers(Ctors)] TEndpoint, TRequest, TItem>(
        [AsParameters] TRequest request, [FromServices] IServiceProvider services, CancellationToken ct)
        where TEndpoint : StreamEndpoint<TRequest, TItem>
        where TRequest : notnull
        => TypedResults.ServerSentEvents(
            Activate<TEndpoint>(services).HandleAsync(request, ct),
            EventTypes<TEndpoint>.Value);

    public static ServerSentEventsResult<TItem> EventStreamFromBody<[DynamicallyAccessedMembers(Ctors)] TEndpoint, TRequest, TItem>(
        [FromBody] TRequest request, [FromServices] IServiceProvider services, CancellationToken ct)
        where TEndpoint : StreamEndpoint<TRequest, TItem>
        where TRequest : notnull
        => TypedResults.ServerSentEvents(
            Activate<TEndpoint>(services).HandleAsync(request, ct),
            EventTypes<TEndpoint>.Value);

    private const DynamicallyAccessedMemberTypes Ctors = DynamicallyAccessedMemberTypes.PublicConstructors;

    // The request's services rather than HttpContext: with an endpoint filter, every argument is carried
    // through the filter pipeline, and an HttpContext argument costs each request ~200 bytes.
    private static TEndpoint Activate<[DynamicallyAccessedMembers(Ctors)] TEndpoint>(IServiceProvider services) where TEndpoint : EndpointBase
        => Infrastructure.GeneratedEndpoints.Activate<TEndpoint>(services);
}
