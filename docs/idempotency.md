# Idempotency

A client sends `POST /api/payments`, the network drops the response, the client retries, and the customer
is charged twice. `Keel.Endpoints.Idempotency` makes the retry safe: with an `Idempotency-Key` header the first
request runs, its response is stored, and every retry with the same key gets that response back without
running the handler again.

```bash
dotnet add package Keel.Endpoints.Idempotency --prerelease
```

```csharp
builder.Services.AddKeelIdempotency();

[Post("payments")]
public sealed class Endpoint(Payments payments) : ResultEndpoint<CreatePayment, Created<Payment>>
{
    public override void Configure(EndpointOptions options) => options.Idempotent();
    // ...
}
```

Or for every `POST` and `PATCH` in a feature folder (an `EndpointGroup` calling `options.Idempotent()`) or
under a route group: `app.MapKeelEndpoints("/api").WithIdempotency()`. `GET`, `PUT` and `DELETE` are
idempotent by definition and pass through.

## What clients get

| Request | Response |
| --- | --- |
| New key | The handler runs; its response is stored for 24 hours (not a 5xx) |
| Same key, same request | The stored status, headers and body, plus `Idempotent-Replayed: true` |
| Same key, different request | `422` problem details: a key names one request |
| Same key while the first still runs | `409` problem details with `Retry-After: 1` |
| No key | Runs normally; `400` with `options.Idempotent(o => o.RequireKey = true)` |
| Key over 255 characters | `400` |

"Same request" compares the bound contract (and route and query values), not the raw bytes: the same JSON
with members in another order is a retry. The `409`, `422` and (when required) `400` responses are
documented in OpenAPI and the API manifest.

Clients should send a new random key (a UUID) for each operation and reuse it only to retry that operation.

## How it behaves

- **Per caller.** A key is scoped to the endpoint and to the authenticated user (`NameIdentifier` claim, or
  the identity name), so one caller can never receive another's stored response.
- **After validation.** The filter runs after authentication, binding and validation. A request rejected
  there does not use up its key.
- **Failures are retryable.** A handler exception or a 5xx releases the key, so the retry runs the handler.
- **Streams pass through.** Server-Sent Events and JSON streams are sent as usual and not stored.
- **Large responses** (over `MaxResponseSize`, 1 MiB) are sent but not stored, with a warning in the log.

## Settings

```csharp
builder.Services.AddKeelIdempotency(o =>
{
    o.Retention = TimeSpan.FromDays(1);          // how long retries are answered from the store
    o.InFlightTimeout = TimeSpan.FromMinutes(1); // how long a running request holds its key; above your slowest handler
    o.HeaderName = "Idempotency-Key";
    o.MaxResponseSize = 1024 * 1024;
});
```

Per endpoint: `options.Idempotent(o => { o.RequireKey = true; o.Retention = TimeSpan.FromHours(1); })`.

## Storage and several instances

Responses live in `IDistributedCache`: in memory by default (one instance), or shared when you register a
distributed cache:

```csharp
builder.Services.AddStackExchangeRedisCache(o => o.Configuration = "localhost:6379");
builder.Services.AddKeelIdempotency();
```

With a shared cache, replays work across instances. Claims are exact within an instance; two requests with
the same key arriving within milliseconds on two different instances can both run, because
`IDistributedCache` has no atomic "set if absent". When that matters, implement `IIdempotencyStore` on an
operation that has one (Redis `SET NX`, a unique row in your database) and register it before
`AddKeelIdempotency()`:

```csharp
builder.Services.AddSingleton<IIdempotencyStore, RedisIdempotencyStore>();
```

It works under Native AOT; CI publishes the AOT sample with an idempotent endpoint and checks a retry.
