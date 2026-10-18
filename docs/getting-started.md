# Getting started

## A new API

```bash
dotnet new install Keel.Endpoints.Templates
dotnet new keel-api -n Shop          # --aot for Native AOT
cd Shop
dotnet run                           # then open http://localhost:5000/scalar
```

The project has one feature, `Features/Todos`, with two operations (`Create` and `Get`), a health check
and an `EndpointGroup` that declares the feature public. Add an operation:

```bash
dotnet new keel-slice -n Rename --feature Todos --method put
```

This writes `Features/Todos/Rename/Endpoint.cs`: a contract bound the right way for `PUT` (the id from the
route, the changes from the body) and an endpoint that answers `204`. Fill in `HandleAsync`.

## An existing API

```bash
dotnet add package Keel.Endpoints --prerelease
```

```csharp
builder.Services.AddKeelEndpoints();
builder.Services.AddValidation();       // .NET 10 validation for contracts
builder.Services.AddProblemDetails();   // RFC 9457 errors

var app = builder.Build();
app.MapKeelEndpoints("/api").RequireAuthorization();   // generated at compile time
```

One folder per operation is the convention (nothing enforces it):

```csharp
namespace Shop.Features.Orders.Get;

public sealed record GetOrder(string OrderId);

[Get("orders/{orderId}")]
public sealed class Endpoint(OrderStore store) : ResultEndpoint<GetOrder, Results<Ok<Order>, NotFound>>
{
    public override async Task<Results<Ok<Order>, NotFound>> HandleAsync(GetOrder request, CancellationToken ct) =>
        await store.FindAsync(request.OrderId, ct) is { } order ? TypedResults.Ok(order) : TypedResults.NotFound();
}
```

Keel endpoints are ordinary Minimal API endpoints, so they sit next to any `MapGet` you already have.

## What next

- Decide who may call each endpoint: [authorization](authorization.md). The app refuses to start until you do.
- Shared configuration for a feature folder: [endpoint groups](endpoints.md#groups).
- Keep the API from breaking clients by accident: [the API manifest](api-manifest.md).
