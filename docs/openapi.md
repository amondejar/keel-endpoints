# OpenAPI

Keel endpoints describe themselves through ASP.NET Core's metadata, so `Microsoft.AspNetCore.OpenApi`,
Swashbuckle or NSwag document them like any Minimal API: routes, parameters, bodies, typed results.

```csharp
builder.Services.AddOpenApi();
app.MapOpenApi();                    // /openapi/v1.json
app.MapScalarApiReference();         // /scalar (Scalar.AspNetCore)
```

## What Keel adds

- **Operation names.** A class named `Endpoint` takes its name from its last two namespace segments
  (`Orders.Get.Endpoint` is `OrdersGet`); any other class uses its name without the `Endpoint` suffix
  (`GetOrderEndpoint` is `GetOrder`). Generated clients use it for method names.
- **Tags from the namespace.** `Orders.Get.Endpoint` is tagged `Orders`. `[Tags]`, `options.Tags(...)` or a
  tag on any group wins; `KeelOptions.TagFromNamespace = false` turns it off.
- **The validation 400**, for endpoints whose contract has rules.

`options.Summary(...)` and attributes on the class (`[EndpointSummary]`, `[EndpointDescription]`,
`[ProducesResponseType]`) become metadata as they do on a controller.

## Security schemes

Declare them with a document transformer; the sample declares its `X-Api-Key` header this way so Scalar
can send it (`samples/Keel.Sample/ApiReference.cs`).
