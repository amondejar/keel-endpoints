namespace Keel.Endpoints;

/// <summary>
/// Common base of every endpoint class. Do not derive from it directly; use one of the
/// shapes below. An endpoint is created per request with constructor injection.
/// </summary>
public abstract class EndpointBase
{
    private protected EndpointBase() { }

    /// <summary>
    /// Configures the endpoint. Called once at map time on an instance created without
    /// dependencies, so it must not read constructor-injected state.
    /// </summary>
    public virtual void Configure(EndpointOptions options) { }
}

/// <summary>A request in, a <c>200 OK</c> response body out.</summary>
public abstract class Endpoint<TRequest, TResponse> : EndpointBase
{
    public abstract Task<TResponse> HandleAsync(TRequest request, CancellationToken cancellationToken);
}

/// <summary>A request in, <c>204 No Content</c> out.</summary>
public abstract class Endpoint<TRequest> : EndpointBase
{
    public abstract Task HandleAsync(TRequest request, CancellationToken cancellationToken);
}

/// <summary>No request contract, a <c>200 OK</c> response body out.</summary>
public abstract class EndpointWithoutRequest<TResponse> : EndpointBase
{
    public abstract Task<TResponse> HandleAsync(CancellationToken cancellationToken);
}

/// <summary>
/// The handler decides the status code by returning a native result type, for example
/// <c>Results&lt;Ok&lt;InvoiceView&gt;, NotFound&gt;</c>. Typed results document every
/// possible response in OpenAPI automatically.
/// </summary>
public abstract class ResultEndpoint<TRequest, TResult> : EndpointBase
    where TResult : IResult
{
    public abstract Task<TResult> HandleAsync(TRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// A request in, a stream of items out, as a JSON array or as Server-Sent Events
/// (see <see cref="EndpointOptions.StreamFormat"/>).
/// </summary>
public abstract class StreamEndpoint<TRequest, TItem> : EndpointBase
{
    public abstract IAsyncEnumerable<TItem> HandleAsync(TRequest request, CancellationToken cancellationToken);
}
