# Versioning

`Keel.Endpoints.Versioning` connects Keel endpoints to [Asp.Versioning](https://github.com/dotnet/aspnet-api-versioning),
the standard versioning library for ASP.NET Core: one folder per version, and nothing to repeat per endpoint.

```bash
dotnet add package Keel.Endpoints.Versioning --prerelease
dotnet add package Asp.Versioning.Mvc.ApiExplorer   # optional: one OpenAPI document and manifest entry per version
```

```csharp
builder.Services.AddApiVersioning(o => o.ReportApiVersions = true)
    .AddApiExplorer(o => { o.GroupNameFormat = "'v'V"; o.SubstituteApiVersionInUrl = true; });

app.NewVersionedApi()
    .MapKeelEndpoints("/api/v{version:apiVersion}")
    .WithKeelVersioning();
```

```text
Features/V1/Orders/Get/Endpoint.cs   GET /api/v1/orders/{id}   operation OrdersGetV1
Features/V2/Orders/Get/Endpoint.cs   GET /api/v2/orders/{id}   operation OrdersGetV2
```

## Where versions come from

1. **The class**: `[ApiVersion(2.0)]`, `[MapToApiVersion(2.0)]` or `[ApiVersionNeutral]` (Asp.Versioning's
   attributes) on the endpoint class. Several `[ApiVersion]` make one class serve several versions.
2. **Conventions**: `options.Convention(b => b.HasApiVersion(2.0))` in `Configure` or in an `EndpointGroup`.
3. **The namespace**, when neither says anything: a segment `V2`, `v2_1` or `v2026_10_01` (`_` works as a
   prefix too, for folders that start with a digit), parsed as Asp.Versioning's `NamespaceParser` does.

Put the version folder above the feature (`Features/V2/Orders/Get`), so the feature tag stays `Orders` and
the operation name `OrdersGet`.

## Operation names

ASP.NET Core requires endpoint names to be unique, and OpenAPI clients use them as method names. Two
versions of `Orders.Get` would both be `OrdersGet`, and the app stops routing. `WithKeelVersioning()` adds the
version to the name of every endpoint that serves exactly one version: `OrdersGetV2`, `OrdersGetV2_1`,
`OrdersGetV2026_10_01`. Endpoints that serve several versions or none keep their name. Turn it off with
`WithKeelVersioning(o => o.VersionedOperationNames = false)` and name endpoints yourself.

## What stays Asp.Versioning's

Reading the version (URL segment, query string `?api-version=`, header, media type), answering
unsupported versions, the `api-supported-versions` and `api-deprecated-versions` headers, deprecation and
sunset policies: all configured in `AddApiVersioning(...)` as its documentation describes.

## Analyzer, manifest and OpenAPI

- **KEEL001** treats the same route in two versions (folders or `[ApiVersion]`) as distinct. Versions set
  only through conventions are invisible to it; prefer folders or attributes.
- **The API manifest** gets one entry per version (`GET api/v1/orders/{id}`, `GET api/v2/orders/{id}`) with
  the versioned ApiExplorer (`AddApiExplorer(o => o.SubstituteApiVersionInUrl = true)`), so a change to v2 is
  never mistaken for a break in v1, and removing v1 shows up as breaking.
- **OpenAPI**: one document per version through the versioned ApiExplorer's group names (`v1`, `v2`).
