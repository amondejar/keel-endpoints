# Keel.Endpoints.Versioning

API versioning for [Keel.Endpoints](https://www.nuget.org/packages/Keel.Endpoints), on top of
[Asp.Versioning](https://github.com/dotnet/aspnet-api-versioning): one folder per version, and nothing to
repeat on each endpoint.

```csharp
builder.Services.AddApiVersioning(o => o.ReportApiVersions = true)
    .AddApiExplorer(o => { o.GroupNameFormat = "'v'V"; o.SubstituteApiVersionInUrl = true; });

app.NewVersionedApi()
    .MapKeelEndpoints("/api/v{version:apiVersion}")
    .WithKeelVersioning();
```

```text
Features/V1/Orders/Get/Endpoint.cs   → GET /api/v1/orders/{id}, operation OrdersGetV1
Features/V2/Orders/Get/Endpoint.cs   → GET /api/v2/orders/{id}, operation OrdersGetV2
```

- **Version from the folder**: `V2`, `V2_1`, `v2026_10_01` in the namespace, as Asp.Versioning reads them.
  `[ApiVersion(2.0)]`, `[MapToApiVersion]` or `[ApiVersionNeutral]` on the class win.
- **Unique operation names**: an endpoint serving one version gets it as a suffix (`OrdersGetV2`); without it
  two versions of an operation have the same name and ASP.NET Core refuses to route.
- **KEEL001** knows about versions: the same route in `V1` and `V2` is not a duplicate.

Routing by version, `api-supported-versions` headers, deprecation and sunset policies are Asp.Versioning's.
See the [versioning guide](https://github.com/amondejar/keel-endpoints/blob/main/docs/versioning.md).
