using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace Keel.Endpoints.Internal;

/// <summary>
/// The handlers handed to ASP.NET Core's RequestDelegateFactory, one per endpoint shape and
/// binding. Because they are ordinary Minimal API handlers, binding, validation, typed results
/// and OpenAPI metadata all come from ASP.NET Core itself.
/// </summary>
internal static class Handlers
{
    // Endpoint<TRequest, TResponse>
    public static Task<TResponse> ResponseFromParameters<TEndpoint, TRequest, TResponse>(
        [AsParameters] TRequest request, HttpContext context, CancellationToken ct)
        where TEndpoint : Endpoint<TRequest, TResponse>
        where TRequest : notnull
        => Activate<TEndpoint>(context).HandleAsync(request, ct);

    public static Task<TResponse> ResponseFromBody<TEndpoint, TRequest, TResponse>(
        [FromBody] TRequest request, HttpContext context, CancellationToken ct)
        where TEndpoint : Endpoint<TRequest, TResponse>
        where TRequest : notnull
        => Activate<TEndpoint>(context).HandleAsync(request, ct);

    // Endpoint<TRequest>
    public static async Task<NoContent> NoContentFromParameters<TEndpoint, TRequest>(
        [AsParameters] TRequest request, HttpContext context, CancellationToken ct)
        where TEndpoint : Endpoint<TRequest>
        where TRequest : notnull
    {
        await Activate<TEndpoint>(context).HandleAsync(request, ct);
        return TypedResults.NoContent();
    }

    public static async Task<NoContent> NoContentFromBody<TEndpoint, TRequest>(
        [FromBody] TRequest request, HttpContext context, CancellationToken ct)
        where TEndpoint : Endpoint<TRequest>
        where TRequest : notnull
    {
        await Activate<TEndpoint>(context).HandleAsync(request, ct);
        return TypedResults.NoContent();
    }

    // EndpointWithoutRequest<TResponse>
    public static Task<TResponse> WithoutRequest<TEndpoint, TResponse>(HttpContext context, CancellationToken ct)
        where TEndpoint : EndpointWithoutRequest<TResponse>
        => Activate<TEndpoint>(context).HandleAsync(ct);

    // ResultEndpoint<TRequest, TResult>
    public static Task<TResult> ResultFromParameters<TEndpoint, TRequest, TResult>(
        [AsParameters] TRequest request, HttpContext context, CancellationToken ct)
        where TEndpoint : ResultEndpoint<TRequest, TResult>
        where TRequest : notnull
        where TResult : IResult
        => Activate<TEndpoint>(context).HandleAsync(request, ct);

    public static Task<TResult> ResultFromBody<TEndpoint, TRequest, TResult>(
        [FromBody] TRequest request, HttpContext context, CancellationToken ct)
        where TEndpoint : ResultEndpoint<TRequest, TResult>
        where TRequest : notnull
        where TResult : IResult
        => Activate<TEndpoint>(context).HandleAsync(request, ct);

    // StreamEndpoint<TRequest, TItem>
    public static IAsyncEnumerable<TItem> JsonStreamFromParameters<TEndpoint, TRequest, TItem>(
        [AsParameters] TRequest request, HttpContext context, CancellationToken ct)
        where TEndpoint : StreamEndpoint<TRequest, TItem>
        where TRequest : notnull
        => Activate<TEndpoint>(context).HandleAsync(request, ct);

    public static IAsyncEnumerable<TItem> JsonStreamFromBody<TEndpoint, TRequest, TItem>(
        [FromBody] TRequest request, HttpContext context, CancellationToken ct)
        where TEndpoint : StreamEndpoint<TRequest, TItem>
        where TRequest : notnull
        => Activate<TEndpoint>(context).HandleAsync(request, ct);

    public static ServerSentEventsResult<TItem> EventStreamFromParameters<TEndpoint, TRequest, TItem>(
        [AsParameters] TRequest request, HttpContext context, CancellationToken ct)
        where TEndpoint : StreamEndpoint<TRequest, TItem>
        where TRequest : notnull
        => TypedResults.ServerSentEvents(
            Activate<TEndpoint>(context).HandleAsync(request, ct),
            EventTypes<TEndpoint>.Value);

    public static ServerSentEventsResult<TItem> EventStreamFromBody<TEndpoint, TRequest, TItem>(
        [FromBody] TRequest request, HttpContext context, CancellationToken ct)
        where TEndpoint : StreamEndpoint<TRequest, TItem>
        where TRequest : notnull
        => TypedResults.ServerSentEvents(
            Activate<TEndpoint>(context).HandleAsync(request, ct),
            EventTypes<TEndpoint>.Value);

    private static TEndpoint Activate<TEndpoint>(HttpContext context) where TEndpoint : EndpointBase
        => Factory<TEndpoint>.Create(context.RequestServices, null);

    private static class Factory<TEndpoint> where TEndpoint : EndpointBase
    {
        public static readonly ObjectFactory<TEndpoint> Create = ActivatorUtilities.CreateFactory<TEndpoint>([]);
    }

    internal static class EventTypes<TEndpoint>
    {
        public static string? Value;
    }
}
