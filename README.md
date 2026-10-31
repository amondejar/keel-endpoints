<img src="assets/icon.png" alt="" width="64" align="right">

# Keel.Endpoints

[![CI](https://github.com/amondejar/keel-endpoints/actions/workflows/ci.yml/badge.svg)](https://github.com/amondejar/keel-endpoints/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/vpre/Keel.Endpoints.svg?label=Keel.Endpoints)](https://www.nuget.org/packages/Keel.Endpoints)
[![Downloads](https://img.shields.io/nuget/dt/Keel.Endpoints.svg)](https://www.nuget.org/packages/Keel.Endpoints)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

**One class per endpoint for ASP.NET Core Minimal APIs.** Binding, validation and OpenAPI stay
native; authorization is fail-closed; analyzers catch route mistakes at build time.

> **Status: 1.0 release candidate.** The public API is what 1.0.0 will ship ([compatibility](docs/compatibility.md)).
> **[Documentation](docs/README.md)**

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

app.MapKeelEndpoints("/api").RequireAuthorization();   // generated at compile time, no reflection
```

```bash
dotnet add package Keel.Endpoints --prerelease
```

Or start from a template:

```bash
dotnet new install Keel.Endpoints.Templates
dotnet new keel-api -n Shop            # add --aot for Native AOT, --mcp for MCP tools
cd Shop && dotnet new keel-slice -n Rename --feature Todos --method put
```

## Why

Minimal APIs are a great runtime and an awkward way to organize a large API. Keel.Endpoints gives each
operation a class, so a feature is one folder: its contract, its handler, its tests. Everything else is
ASP.NET Core: each endpoint is registered as an ordinary Minimal API handler, so ASP.NET Core binds it,
.NET 10 validates it, typed results document it, and every `RouteHandlerBuilder` convention you already
know still applies.

## Registration and Native AOT

`app.MapKeelEndpoints(prefix)` is **generated at compile time**: a source generator in the package writes
one explicit, typed `MapMethods` call per endpoint class in the project. No assembly scanning, no
`MakeGenericMethod`, nothing for the trimmer to remove.

With `<PublishAot>true</PublishAot>` the package goes one step further. ASP.NET Core's Request Delegate
Generator, which makes Minimal API binding AOT-safe, cannot see code written by another generator, so the
build compiles the project once with generated files written to disk and adds Keel's registration as
ordinary source. The Request Delegate Generator then generates native binding for every endpoint, and the
.NET 10 validation generator finds every contract on its own (no `[ValidatableType]` needed). CI publishes
[`samples/Keel.AotSample`](samples/Keel.AotSample) natively with every warning as an error and calls each
endpoint.

| | Generated `MapKeelEndpoints(prefix)` | Reflective `MapKeelEndpoints(assembly, prefix)` |
| --- | --- | --- |
| Discovery | Compile time | Startup, `Assembly.GetTypes()` |
| Trimming / Native AOT | Supported | Not supported (annotated `[RequiresUnreferencedCode]`) |
| Endpoints from other projects | Only this project's | Any assembly you pass |
| Binding / stream format | From the HTTP method, `[BindRequest]`, `[ServerSentEvents]` | Also from `Configure` |

The generated registration covers the endpoint classes in the project that calls it. Under Native AOT, JSON
types still need a `JsonSerializerContext` (see the AOT sample), as in any AOT Minimal API.
`KeelRegistrationFile` turns the two-step build on or off explicitly.

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
in `Map*` calls in ordinary source, which is why endpoint-class contracts need the attribute; analyzer
**KEEL004** tells you when it is missing. Under Native AOT the registration is ordinary source, so the
attribute is not needed there and KEEL004 stays quiet.

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

## Endpoint groups

Configuration shared by a feature folder goes in an `EndpointGroup`. A group applies to every endpoint in
its namespace and the namespaces below it:

```csharp
namespace Billing.Invoices;

public sealed class InvoicesGroup : EndpointGroup
{
    public override void Configure(EndpointOptions options) =>
        options.RequireAuthorization("billing").Tags("Invoices");
}
```

Groups run outermost namespace first, then the endpoint's own `Configure`, so the endpoint has the last
word. A group's authorization decision counts for fail-closed. Binding and stream format belong to each
endpoint and are rejected in a group. A group needs a public parameterless constructor (KEEL006). Groups
work the same with the generated registration, under Native AOT and with the reflective one.

## Streaming

A `StreamEndpoint` writes a JSON array by default. Mark it `[ServerSentEvents]` for `text/event-stream`:

```csharp
[Get("invoices/events"), ServerSentEvents("invoice")]
public sealed class Endpoint : StreamEndpoint<WatchInvoices, InvoiceEvent>
{
    public override async IAsyncEnumerable<InvoiceEvent> HandleAsync(WatchInvoices request,
        [EnumeratorCancellation] CancellationToken ct) { /* yield return ... */ }
}
```

## OpenAPI

The sample shows the full setup: `AddOpenApi()`, `MapOpenApi()` and a [Scalar](https://scalar.com) API
reference (`MapScalarApiReference()`), with the API key header declared. Run it and the browser opens
`/scalar`.

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
| KEEL005 | An endpoint class the generated registration cannot reach (private or nested in a private type) |
| KEEL006 | An endpoint group the generated registration cannot create (no public parameterless constructor, or not accessible) |

KEEL002, KEEL003 and KEEL004 come with code fixes (the light bulb in Visual Studio, Rider and VS Code):
add the missing contract member, bind the contract from parameters with
`[BindRequest(RequestBinding.Parameters)]`, or add `[ValidatableType]`.

## Templates

`Keel.Endpoints.Templates` installs two `dotnet new` templates:

| Template | Creates |
| --- | --- |
| `keel-api` | An API with a sample feature, validation, ProblemDetails, OpenAPI and Scalar (`/scalar` in Development). `--aot` adds Native AOT: slim host and a `JsonSerializerContext`. `--mcp` offers the todo endpoints to AI agents as MCP tools at `/mcp`. |
| `keel-slice` | One operation as `Features/<Feature>/<Name>/Endpoint.cs` with its contract. Options: `--feature` (required), `--method get\|post\|put\|patch\|delete`, `--route`, `--folder`. |

A new slice starts fail-closed: the app will not start until you decide who may call it (on the class, in
an `EndpointGroup` for the feature, or on the route group).

Under `--aot`, OpenAPI and Scalar run only in Development; if a version of either reports trimming
warnings on publish, they do not affect the endpoints.

## Testing

`Keel.Endpoints.Testing` calls endpoints by type, so tests cannot drift from routes:

```csharp
var client = new EndpointClient(factory.CreateClient(), prefix: "/api");
var response = await client.SendAsync<Invoices.Get.Endpoint, InvoiceView>(new GetInvoice("42"));
Assert.Equal(HttpStatusCode.OK, response.StatusCode);
```

## API manifest: breaking changes fail CI

`api.manifest.json` records the HTTP surface clients depend on: every route, its authorization,
parameters, body and responses, and the JSON shape of each type. One test keeps it honest:

```csharp
public class ApiManifestTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public void Api_matches_its_manifest() => ApiManifest.Verify(factory.Services);
}
```

The first run writes the file next to the test; commit it. From then on any API change fails the test
with a list, and the manifest diff shows up in the pull request:

```text
breaking   POST api/invoices: request body.currency added as required
breaking   GET api/invoices/{invoiceId}: response 200.status may now be null
security   DELETE api/invoices/{invoiceId}: no longer requires policy 'admin'
compatible GET api/invoices: response 200[].currency added
```

Accept intended changes with `KEEL_UPDATE_MANIFEST=1 dotnet test` (compatible changes only) or
`KEEL_UPDATE_MANIFEST=all`, then commit the file. CI never sets it.

`dotnet keel diff v1.0.0` (`Keel.Endpoints.Tool`) lists the API changes between two versions, and the
[api-diff action](actions/api-diff) comments them on every pull request. The rules are in
[docs/api-manifest.md](docs/api-manifest.md).

## Versioning

`Keel.Endpoints.Versioning` puts each version in its own folder (`Features/V2/...`) on top of Asp.Versioning,
with a unique operation name per version:

```csharp
app.NewVersionedApi().MapKeelEndpoints("/api/v{version:apiVersion}").WithKeelVersioning();
```

See [docs/versioning.md](docs/versioning.md).

## Idempotency

`Keel.Endpoints.Idempotency` makes retries of `POST` and `PATCH` safe: with an `Idempotency-Key` header the
first response is stored and retries get it back without running the handler again.

```csharp
builder.Services.AddKeelIdempotency();
public override void Configure(EndpointOptions options) => options.Idempotent();
```

See [docs/idempotency.md](docs/idempotency.md).

## MCP tools for AI agents

`Keel.Endpoints.Mcp` offers endpoints to AI agents as [MCP](https://modelcontextprotocol.io) tools, on the official
MCP C# SDK. The contract is the tool's schema; a call runs the endpoint in process, with its validation and
authorization, as the calling user. Writes must say whether they are destructive.

```csharp
builder.Services.AddKeelMcp();
app.MapKeelMcp("/mcp");
public override void Configure(EndpointOptions options) => options.ExposeAsTool("Gets an invoice by its number.");
```

The API manifest records each tool, so `dotnet keel diff` flags a renamed tool or a write that stopped asking for
confirmation; an optional MCP Server Card (`/mcp/server-card`) makes the server discoverable. See
[docs/mcp.md](docs/mcp.md).

## Observability

`Keel.Endpoints.OpenTelemetry` tags ASP.NET Core's request spans and `http.server.request.duration` with
the operation and feature, counts validation errors per field, and can time each handler on its own:

```csharp
builder.Services.AddKeelTelemetry();
builder.Services.AddOpenTelemetry()
    .WithTracing(t => t.AddAspNetCoreInstrumentation().AddKeelEndpointsInstrumentation())
    .WithMetrics(m => m.AddAspNetCoreInstrumentation().AddKeelEndpointsInstrumentation());
```

See [docs/observability.md](docs/observability.md).

## FluentValidation

Prefer FluentValidation over DataAnnotations? Add `Keel.Endpoints.FluentValidation`: an ordinary endpoint
filter that runs each argument's registered `IValidator<T>` and returns the same `400` problem details.

```csharp
builder.Services.AddKeelFluentValidation(typeof(Program).Assembly);
app.MapKeelEndpoints("/api").RequireAuthorization().WithFluentValidation();
```

See [its README](src/Keel.Endpoints.FluentValidation/README.md).

## Roadmap

- **0.2** Native AOT: generated registration (done, `0.2.0-preview`).
- **0.3** Group configuration per folder, `dotnet new` templates, code fixes (done, `0.3.0-preview`).
- **0.4** A route and contract manifest with breaking-change checks in CI (done, `0.4.0-preview`).
- **1.0** Public API frozen ([PublicAPI.txt](src/Keel.Endpoints/PublicAPI.txt), package validation), [full docs](docs/README.md), [benchmark](benchmarks/Keel.Endpoints.Benchmarks) within 5% of a hand-written Minimal API, a [complete example application](https://github.com/amondejar/keel-shop) (release candidate: `1.0.0-rc.1`).

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
