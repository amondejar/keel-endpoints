# Observability

ASP.NET Core already emits a span per request and the `http.server.request.duration` metric, tagged with the
route. `Keel.Endpoints.OpenTelemetry` adds what only Keel knows: which operation and feature a request
belongs to, which members fail validation, and how long the handler itself takes.

```bash
dotnet add package Keel.Endpoints.OpenTelemetry --prerelease
dotnet add package OpenTelemetry.Extensions.Hosting
dotnet add package OpenTelemetry.Instrumentation.AspNetCore
dotnet add package OpenTelemetry.Exporter.OpenTelemetryProtocol
```

```csharp
builder.Services.AddProblemDetails();   // validation errors are counted as they become problem details
builder.Services.AddKeelTelemetry();

builder.Services.AddOpenTelemetry()
    .WithTracing(t => t.AddAspNetCoreInstrumentation().AddKeelEndpointsInstrumentation())
    .WithMetrics(m => m.AddAspNetCoreInstrumentation().AddKeelEndpointsInstrumentation())
    .UseOtlpExporter();                 // OTEL_EXPORTER_OTLP_ENDPOINT, default http://localhost:4317

var app = builder.Build();
app.MapKeelEndpoints("/api").RequireAuthorization().WithHandlerTelemetry();   // optional, see below
```

## What you get

| Signal | Name | Tags | From |
| --- | --- | --- | --- |
| Request span (ASP.NET Core's) | `GET api/orders/{id}` | `keel.operation`, `keel.feature` added | `AddKeelTelemetry()` |
| Request duration (ASP.NET Core's histogram) | `http.server.request.duration` | `keel.operation`, `keel.feature` added | `AddKeelTelemetry()` |
| Validation errors (counter) | `keel.validation.errors` | `keel.operation`, `keel.feature`, `keel.field` | `AddKeelTelemetry()` |
| Handler span | the operation name, `OrdersGet` | `keel.operation`, `keel.feature`, `error.type` | `WithHandlerTelemetry()` |
| Handler duration (histogram, seconds) | `keel.handler.duration` | `keel.operation`, `keel.feature`, `error.type` | `WithHandlerTelemetry()` |
| Logging scope | | `keel.operation`, `keel.feature` on every log the handler writes | `WithHandlerTelemetry()` |

`keel.operation` is the OpenAPI operation name (`OrdersGet`) and `keel.feature` comes from the namespace
(`Orders`), so dashboards group by feature folder, not only by route. Endpoints that are not Keel endpoints
are left as ASP.NET Core describes them.

Status codes are already on `http.server.request.duration` (`http.response.status_code`), so 401s, 403s and
400s per operation need no extra metric: filter that one by `keel.operation`.

## Choosing what to turn on

- **`AddKeelTelemetry()`** costs two tags per request and no endpoint filter. Turn it on everywhere.
- **`WithHandlerTelemetry()`** wraps each handler in an endpoint filter. Binding and validation run before
  it, so its span and duration are the handler's own: "is it my code or the framework?" On the benchmark's
  2-core VM it added about 0.2 KB per request with nothing listening, and about 0.8 KB and 1 µs with every
  span recorded. Sample traces in production, or put it only on the groups you are investigating:
  `app.MapGroup("/api/orders").WithHandlerTelemetry()`.

## Seeing it locally

The .NET Aspire dashboard takes OTLP directly:

```bash
docker run --rm -it -p 18888:18888 -p 4317:18889 -e DOTNET_DASHBOARD_UNSECURED_ALLOW_ANONYMOUS=true mcr.microsoft.com/dotnet/aspire-dashboard
```

Run the API and open `http://localhost:18888`: traces show the handler span under each request, and the
metrics page lists `keel.validation.errors` and `keel.handler.duration`.

Without OpenTelemetry at all, `dotnet-counters` reads the same meters:

```bash
dotnet-counters monitor -n Shop --counters Keel.Endpoints,Microsoft.AspNetCore.Hosting
```

## Queries

With a Prometheus backend, OpenTelemetry names become `http_server_request_duration_seconds` and
`keel_validation_errors_total`, and tags become labels:

```promql
# p95 latency per feature
histogram_quantile(0.95, sum by (le, keel_feature) (rate(http_server_request_duration_seconds_bucket[5m])))

# Share of failed requests per operation
sum by (keel_operation) (rate(http_server_request_duration_seconds_count{http_response_status_code=~"5.."}[5m]))
  / sum by (keel_operation) (rate(http_server_request_duration_seconds_count[5m]))

# The fields clients get wrong most
topk(10, sum by (keel_operation, keel_field) (increase(keel_validation_errors_total[24h])))
```

## Other backends

Everything is plain `System.Diagnostics`: the `ActivitySource` and the `Meter` are both named
`Keel.Endpoints` (`KeelTelemetry.ActivitySourceName`, `KeelTelemetry.MeterName`). Azure Monitor's
OpenTelemetry distro, for example, takes the same two `AddKeelEndpointsInstrumentation()` calls after
`UseAzureMonitor()`. It all works under Native AOT; CI publishes the AOT sample with it.
