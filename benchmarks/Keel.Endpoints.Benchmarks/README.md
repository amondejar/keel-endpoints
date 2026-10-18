# Benchmarks

Keel endpoints against the same operations written as plain Minimal API lambdas:

| Scenario | Minimal API | Keel |
| --- | --- | --- |
| `GET things/{id}` | `(string id, ThingStore store) => TypedResults.Ok(store.Get(id))` | `Endpoint<GetThing, Thing>` with `ThingStore` injected |
| `POST things` | `(CreateThing request, ThingStore store) => TypedResults.Created(...)`, validated | `ResultEndpoint<CreateThing, Created<Thing>>`, validated |

Each request delegate is invoked in process, as routing would after matching, so the numbers are the
framework's own cost (binding, validation, the handler, JSON) without the network. Variants run
interleaved, round after round, and the table shows the median.

```bash
dotnet run -c Release --project benchmarks/Keel.Endpoints.Benchmarks             # 21 rounds x 100,000 requests
dotnet run -c Release --project benchmarks/Keel.Endpoints.Benchmarks -- --check 5 # exit 1 if Keel is > 5% slower
dotnet run -c Release --project benchmarks/Keel.Endpoints.Benchmarks -- --quick   # a smoke run
```

Run it on a quiet machine with the power plan on performance: on a shared 2-core VM the same build moves by
±5% from run to run. CI runs `--quick` only, to keep the benchmark compiling and working; the 1.0 target
(Keel within 5% of Minimal API) is checked on dedicated hardware before each release.

## Results

A 2-core cloud VM, .NET 10, three full runs (ns per request, median; allocated bytes per request):

| Scenario | Minimal API | Keel (generated) | Keel (reflective) | Bytes (Minimal / Keel) |
| --- | --- | --- | --- | --- |
| `GET things/{id}` | 1,643–1,701 | 1,688–1,789 (+2–6%) | 1,703–1,754 | 2,472 / 2,560 |
| `POST things` | 4,757–4,911 | 4,795–5,103 (0–5%) | 4,703–5,205 | 4,720 / 4,816 |

Keel adds about 90 bytes per request: the endpoint instance and the `Task` its handler returns.

What the benchmark found along the way (fixed in 1.0.0-preview.1):

- With `AddValidation()`, ASP.NET Core wrapped every endpoint whose contract is bound with `[AsParameters]`
  in its validation filter, even with nothing to validate: about 900 bytes and 50% more time per request.
  Keel now calls `DisableValidation()` on endpoints whose contract has no rule.
- Under a filter, an `HttpContext` handler argument cost about 200 bytes per request. Keel's handlers now
  take the request's `IServiceProvider` instead.
