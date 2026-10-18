# Endpoints

An endpoint is a class with a route attribute and a base type that says what goes in and what comes out.
Keel registers it as an ordinary Minimal API handler, so ASP.NET Core does the binding, the results and the
metadata.

## Shapes

| Base type | Handler | Response |
| --- | --- | --- |
| `Endpoint<TRequest, TResponse>` | `Task<TResponse> HandleAsync(TRequest, CancellationToken)` | `200` with the body |
| `Endpoint<TRequest>` | `Task HandleAsync(TRequest, CancellationToken)` | `204` |
| `EndpointWithoutRequest<TResponse>` | `Task<TResponse> HandleAsync(CancellationToken)` | `200` with the body |
| `ResultEndpoint<TRequest, TResult>` | `Task<TResult> HandleAsync(TRequest, CancellationToken)` | Whatever the typed result says |
| `StreamEndpoint<TRequest, TItem>` | `IAsyncEnumerable<TItem> HandleAsync(TRequest, CancellationToken)` | A JSON array, or Server-Sent Events |

Use `ResultEndpoint` with `Results<Ok<T>, NotFound, Conflict>` and the like when the status depends on the
outcome: every case is documented in OpenAPI.

## Routes

`[Get]`, `[Post]`, `[Put]`, `[Patch]` and `[Delete]` take a route template, relative to the prefix passed to
`MapKeelEndpoints`. Templates are ordinary ASP.NET Core templates, constraints included
(`orders/{id:int}`). Analyzer KEEL001 reports two endpoints on the same method and template.

## Binding

| Method | The contract is read from | Like |
| --- | --- | --- |
| `GET`, `DELETE` | Route values and the query string | `[AsParameters]` |
| `POST`, `PUT`, `PATCH` | The JSON body | `[FromBody]` |

`[BindRequest(RequestBinding.Parameters)]` or `[BindRequest(RequestBinding.Body)]` on the contract overrides
the default. To take an id from the route and the rest from the body, bind as parameters and mark the
body member:

```csharp
[BindRequest(RequestBinding.Parameters), ValidatableType]
public sealed record RenameOrder(string OrderId, [property: FromBody] RenameOrderBody Body);

[ValidatableType]
public sealed record RenameOrderBody([property: Required, StringLength(100)] string Name);
```

Members of a parameters-bound contract take any binding attribute: `[FromHeader]`, `[FromQuery(Name = ...)]`,
`[FromServices]`. KEEL002 reports a route parameter the contract has no member for; KEEL003 a route
parameter on a contract read from the body. Both have code fixes.

## Dependency injection

An endpoint is created for every request, with constructor injection from the request's services: inject
scoped services (a `DbContext`) freely. Nothing is cached between requests.

## Configure

`Configure` runs once, when the endpoint is mapped:

```csharp
public override void Configure(EndpointOptions options)
{
    options.Summary("Creates an order").Tags("Orders");
    options.RequireAuthorization("orders:write");
    options.Convention(b => b.RequireRateLimiting("writes").CacheOutput());
}
```

`Convention` takes any `RouteHandlerBuilder` call ASP.NET Core offers (rate limiting, output caching, CORS,
filters, metadata). `Configure` runs on an instance created without its constructor, so it must not touch
injected state.

## Groups

An `EndpointGroup` configures every endpoint in its namespace and the namespaces below it:

```csharp
namespace Shop.Features.Orders;

public sealed class OrdersGroup : EndpointGroup
{
    public override void Configure(EndpointOptions options) =>
        options.RequireAuthorization("orders").Tags("Orders");
}
```

Groups run from the outermost namespace in, then the endpoint's own `Configure`. A group needs a public
parameterless constructor (KEEL006) and cannot set `Binding`, `StreamFormat` or `EventType`: those belong to
each endpoint.

## Streaming

A `StreamEndpoint` writes a JSON array, flushed as items are produced. `[ServerSentEvents("event-type")]`
on the class switches it to `text/event-stream`, one event per item:

```csharp
[Get("orders/events"), ServerSentEvents("order")]
public sealed class Endpoint(OrderFeed feed) : StreamEndpoint<WatchOrders, OrderEvent>
{
    public override async IAsyncEnumerable<OrderEvent> HandleAsync(WatchOrders request,
        [EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var e in feed.ReadAsync(ct)) yield return e;
    }
}
```

## Registration

`app.MapKeelEndpoints(prefix)` is generated at compile time and maps every endpoint class in the project. It
returns the `RouteGroupBuilder`, so group-wide conventions are plain ASP.NET Core:

```csharp
app.MapKeelEndpoints("/api").RequireAuthorization().WithFluentValidation();
```

`app.MapKeelEndpoints(assembly, prefix)` discovers endpoints by reflection instead (other assemblies, plugin
scenarios). It is not trimming-safe; see [Native AOT](native-aot.md). `app.MapEndpoint<T>()` maps one class.
