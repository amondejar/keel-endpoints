# Keel.Endpoints

**One class per endpoint for ASP.NET Core Minimal APIs.** Binding, validation and OpenAPI stay
native; authorization is fail-closed; analyzers catch route mistakes at build time.

> **Status: 0.1 preview.** The API may change before 1.0. See the [roadmap](#roadmap).

```csharp
namespace Billing.Invoices.Get;

public sealed record GetInvoice(string InvoiceId);

[Get("invoices/{invoiceId}")]
public sealed class Endpoint(InvoiceStore store)
    : ResultEndpoint<GetInvoice, Results<Ok<InvoiceView>, NotFound>>
{
    public override Task<Results<Ok<InvoiceView>, NotFound>> HandleAsync(GetInvoice request, CancellationToken ct) =>
        Task.FromResult<Results<Ok<InvoiceView>, NotFound>>(
            store.Find(request.InvoiceId) is { } invoice ? TypedResults.Ok(invoice) : TypedResults.NotFound());
}
```

```csharp
builder.Services.AddKeelEndpoints();
builder.Services.AddValidation();       // .NET 10 native validation
builder.Services.AddProblemDetails();   // RFC 9457 errors

app.MapKeelEndpoints(typeof(Program).Assembly, "/api").RequireAuthorization();
```

```bash
dotnet add package Keel.Endpoints --prerelease
```

## Why

Minimal APIs are a great runtime and an awkward way to organize a large API. Keel.Endpoints gives each
operation a class, so a feature is one folder: its contract, its handler, its tests. Everything else is
ASP.NET Core: each endpoint is registered as an ordinary Minimal API handler, so `RequestDelegateFactory`
binds it, .NET 10 validates it, typed results document it, and every `RouteHandlerBuilder` convention
you already know still applies.

## Endpoint shapes

| Base type | For | Response |
| --- | --- | --- |
| `Endpoint<TRequest, TResponse>` | A request in, a body out | 200 |
| `Endpoint<TRequest>` | A command, no body out | 204 |
| `EndpointWithoutRequest<TResponse>` | No contract, a body out | 200 |
| `ResultEndpoint<TRequest, TResult>` | The handler picks the status with `Results<...>` | As typed |
| `StreamEndpoint<TRequest, TItem>` | A stream of items | JSON array or Server-Sent Events |

## Binding

`GET` and `DELETE` bind the contract from route and query, as `[AsParameters]` does. `POST`, `PUT` and
`PATCH` read it from the JSON body. To combine a route value with a body, bind as parameters and mark the
body member:

```csharp
[BindRequest(RequestBinding.Parameters), ValidatableType]
public sealed record RenameInvoice(string InvoiceId, [property: FromBody] RenameInvoiceBody Body);
```

## Validation (.NET 10)

Call `AddValidation()` and mark contracts with `[ValidatableType]`. DataAnnotations run before the
handler, and failures return `400` problem details. The .NET 10 generator only discovers types it sees
in `Map*` calls, which is why endpoint-class contracts need the attribute; analyzer **KEEL004** tells you
when it is missing.

## Fail-closed authorization

By default the application refuses to start if an endpoint has neither authorization metadata nor
`AllowAnonymous`. A decision on the route group, on the class (`[Authorize]`, `[AllowAnonymous]`) or in
`Configure` all count:

```csharp
public override void Configure(EndpointOptions options) => options.RequireAuthorization("admin");
```

Turn it off with `AddKeelEndpoints(o => o.RequireExplicitAuthorization = false)`.

## Per-endpoint configuration

```csharp
public override void Configure(EndpointOptions options)
{
    options.Summary("Creates an invoice").Tags("Invoices");
    options.Convention(b => b.RequireRateLimiting("invoices").CacheOutput());
}
```

`Configure` runs once at map time on an instance created without its constructor, so it must not read
injected state.

## Streaming

```csharp
[Get("invoices/events")]
public sealed class Endpoint : StreamEndpoint<WatchInvoices, InvoiceEvent>
{
    public override void Configure(EndpointOptions options) =>
        (options.StreamFormat, options.EventType) = (StreamFormat.ServerSentEvents, "invoice");

    public override async IAsyncEnumerable<InvoiceEvent> HandleAsync(WatchInvoices request,
        [EnumeratorCancellation] CancellationToken ct) { /* yield return ... */ }
}
```

## OpenAPI

Endpoints describe themselves through ASP.NET Core's own metadata, so `AddOpenApi()` / `MapOpenApi()`
(or Swashbuckle, NSwag, Scalar) document routes, parameters, bodies and every typed result. Keel adds two
things the shared handler would otherwise get wrong or leave out:

- **Tags from the namespace.** `Billing.Invoices.Get.Endpoint` is tagged `Invoices`. A tag declared with
  `[Tags]`, `options.Tags(...)` or on the group wins. Turn it off with `KeelOptions.TagFromNamespace = false`.
- **The validation 400.** Contracts validated by .NET 10 (`[ValidatableType]` with `AddValidation()`) and
  endpoints validated by `WithFluentValidation()` document their `400` validation problem response.

## Analyzers

Shipped inside the package. See [docs/diagnostics.md](docs/diagnostics.md).

| Rule | Detects |
| --- | --- |
| KEEL001 | Two endpoints on the same method and template |
| KEEL002 | A route parameter with no matching contract member |
| KEEL003 | A route parameter on a contract read from the body |
| KEEL004 | A contract with validation rules but no `[ValidatableType]` |

## Testing

`Keel.Endpoints.Testing` calls endpoints by type, so tests cannot drift from routes:

```csharp
var client = new EndpointClient(factory.CreateClient(), prefix: "/api");
var response = await client.SendAsync<Invoices.Get.Endpoint, InvoiceView>(new GetInvoice("42"));
Assert.Equal(HttpStatusCode.OK, response.StatusCode);
```

## FluentValidation

Prefer FluentValidation over DataAnnotations? Add `Keel.Endpoints.FluentValidation`: an ordinary endpoint
filter that runs each argument's registered `IValidator<T>` and returns the same `400` problem details.

```csharp
builder.Services.AddKeelFluentValidation(typeof(Program).Assembly);
app.MapKeelEndpoints(typeof(Program).Assembly, "/api").RequireAuthorization().WithFluentValidation();
```

See [its README](src/Keel.Endpoints.FluentValidation/README.md).

## Roadmap

- **0.2** Native AOT: a source generator for explicit registration.
- **0.3** Group configuration per folder, `dotnet new` templates, code fixes.
- **0.4** A route and contract manifest with breaking-change checks in CI.
- **1.0** Public API frozen for two previews, full docs, benchmark within 5% of a hand-written Minimal API.

## Building

```bash
dotnet build
dotnet run --project tests/Keel.Endpoints.Tests
```

To try everything locally (sample API, analyzers, the package in your own project) see
[docs/testing-locally.md](docs/testing-locally.md).

Without access to nuget.org, add `-p:KeelOfflineRoslyn=true` to compile the analyzers against the
Roslyn assemblies bundled with the .NET SDK.

## Acknowledgements

The endpoint-class shape was popularized by [FastEndpoints](https://fast-endpoints.com) and refined by
[NativeEndpoints](https://github.com/valence-works/NativeEndpoints), whose "ordinary ASP.NET Core
underneath" idea inspired this project. Keel.Endpoints is an independent implementation and shares no code
with either.

## License

[MIT](LICENSE)
