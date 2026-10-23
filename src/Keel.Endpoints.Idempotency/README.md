# Keel.Endpoints.Idempotency

Safe retries for `POST` and `PATCH`. A client sends `Idempotency-Key: <uuid>`; the first request runs and its
response is stored; a retry with the same key gets that response back (with `Idempotent-Replayed: true`)
without running the handler again. No double charges when the network drops the first response.

```csharp
builder.Services.AddKeelIdempotency();                 // IDistributedCache: memory, or Redis if registered

[Post("payments")]
public sealed class Endpoint(Payments payments) : ResultEndpoint<CreatePayment, Created<Payment>>
{
    public override void Configure(EndpointOptions options) => options.Idempotent();
    // ...
}

// or for every POST and PATCH under a group:
app.MapKeelEndpoints("/api").WithIdempotency();
```

| Case | Answer |
| --- | --- |
| New key | Runs the handler; stores the response (unless 5xx) for 24 hours |
| Same key, same request | The stored response, `Idempotent-Replayed: true` |
| Same key, different request | `422` problem details |
| Same key while the first is running | `409` problem details, `Retry-After: 1` |
| No key | Runs normally; `400` with `options.Idempotent(o => o.RequireKey = true)` |

Keys are scoped to the endpoint and the authenticated user. See the
[idempotency guide](https://github.com/amondejar/keel-endpoints/blob/main/docs/idempotency.md).
