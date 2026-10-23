# Keel.Endpoints.OpenTelemetry

Observability for [Keel.Endpoints](https://www.nuget.org/packages/Keel.Endpoints), on top of what ASP.NET
Core already emits.

```csharp
builder.Services.AddKeelTelemetry();

builder.Services.AddOpenTelemetry()
    .WithTracing(t => t.AddAspNetCoreInstrumentation().AddKeelEndpointsInstrumentation())
    .WithMetrics(m => m.AddAspNetCoreInstrumentation().AddKeelEndpointsInstrumentation())
    .UseOtlpExporter();

app.MapKeelEndpoints("/api").RequireAuthorization().WithHandlerTelemetry();   // optional
```

| What | Where |
| --- | --- |
| `keel.operation`, `keel.feature` | On ASP.NET Core's request span and on `http.server.request.duration` |
| `keel.validation.errors` (counter) | One per member that failed validation, tagged with `keel.field` (needs `AddProblemDetails()`) |
| Handler span, `keel.handler.duration` (histogram, seconds) | With `WithHandlerTelemetry()`: the handler alone, after binding and validation |
| Logging scope | With `WithHandlerTelemetry()`: `keel.operation` and `keel.feature` on logs written by the handler |

Built on `System.Diagnostics` (`ActivitySource` `Keel.Endpoints`, `Meter` `Keel.Endpoints`), so it works with
OpenTelemetry, Application Insights or `dotnet-counters`, and under Native AOT. See the
[observability guide](https://github.com/amondejar/keel-endpoints/blob/main/docs/observability.md).
