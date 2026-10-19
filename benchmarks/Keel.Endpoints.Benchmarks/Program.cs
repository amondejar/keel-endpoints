using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Text;
using Keel.Endpoints;
using Keel.Endpoints.Benchmarks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Validation;

// Usage: dotnet run -c Release --project benchmarks/Keel.Endpoints.Benchmarks [-- --check 5] [--quick]
//   --check N   exit 1 if a Keel endpoint (generated registration) is more than N% slower than Minimal API
//   --quick     fewer rounds, for a smoke run in CI
var check = args.SkipWhile(a => a != "--check").Skip(1).Select(double.Parse).FirstOrDefault(double.NaN);
var quick = args.Contains("--quick");
var rounds = quick ? 5 : 21;
var operationsPerRound = quick ? 20_000 : 100_000;

var builder = WebApplication.CreateSlimBuilder(["--Logging:LogLevel:Default=None"]);
builder.Services.AddKeelEndpoints();
if (!args.Contains("--novalidation")) builder.Services.AddValidation();
builder.Services.AddSingleton<ThingStore>();
await using var app = builder.Build();

// The same two operations three ways: Minimal API lambdas, Keel's generated registration, and Keel's
// reflective registration.
app.MapGet("/minimal/things/{id}", (string id, ThingStore store) => TypedResults.Ok(store.Get(id)));
app.MapPost("/minimal/things", (CreateThing request, ThingStore store) => TypedResults.Created($"/things/{request.Name}", store.Create(request.Name)));
app.MapKeelEndpoints("/keel");
#pragma warning disable IL2026, IL3050 // the reflective registration is what is being measured here
app.MapKeelEndpoints(typeof(Program).Assembly, "/reflective");
#pragma warning restore IL2026, IL3050
// The server is never started: request delegates are invoked directly, so no port is opened and
// launchSettings.json (an https:// URL added by an IDE, say) does not matter.

var scenarios = new[]
{
    new Scenario("GET  things/{id}  (route value, JSON response)", "GET", "things/{id}", new() { ["id"] = "42" }, null),
    new Scenario("POST things       (JSON body, validation, 201)", "POST", "things", [], """{"name":"anvil","weight":12}"""),
};

Console.WriteLine($"Keel.Endpoints vs Minimal API, {rounds} rounds x {operationsPerRound:N0} requests, median ns per request\n");
Console.WriteLine($"{"Scenario",-50} {"Minimal",9} {"Keel",9} {"ratio",7} {"Reflective",11} {"ratio",7} {"alloc B (M/K/R)",18}");

var failed = false;
foreach (var scenario in scenarios)
{
    var variants = new[] { "minimal", "keel", "reflective" }
        .Select(prefix => Invoker.Create(app, scenario, prefix))
        .ToArray();

    foreach (var v in variants) await v.VerifyAsync();                 // same status code and body everywhere
    foreach (var v in variants) await v.RunAsync(operationsPerRound);  // warm-up: tiered JIT and PGO

    var samples = variants.Select(_ => new List<double>()).ToArray();
    for (var round = 0; round < rounds; round++)
    {
        // Interleaved, so drift (thermal, background work) hits every variant alike.
        for (var i = 0; i < variants.Length; i++) samples[i].Add(await variants[i].RunAsync(operationsPerRound));
    }

    var medians = samples.Select(Median).ToArray();
    var allocations = await Task.WhenAll(variants.Select(v => v.AllocatedBytesPerRequestAsync()));
    var ratio = medians[1] / medians[0];
    Console.WriteLine(string.Join(" ", medians.Select(m => m.ToString("F0"))) + " | " + string.Join("/", allocations));
    Console.WriteLine($"{scenario.Name,-50} {medians[0],9:F0} {medians[1],9:F0} {ratio,7:F3} {medians[2],11:F0} {medians[2] / medians[0],7:F3} {string.Join("/", allocations),18}");

    if (!double.IsNaN(check) && ratio > 1 + check / 100) failed = true;
}

if (failed)
{
    Console.WriteLine($"\nFAIL: a Keel endpoint is more than {check}% slower than the Minimal API equivalent.");
    return 1;
}

if (!double.IsNaN(check)) Console.WriteLine($"\nPASS: every Keel endpoint is within {check}% of the Minimal API equivalent.");
return 0;

static double Median(List<double> values)
{
    var sorted = values.Order().ToArray();
    return sorted[sorted.Length / 2];
}

namespace Keel.Endpoints.Benchmarks
{
    public sealed record Thing(string Id, string Name, int Version);

    [ValidatableType]
    public sealed record CreateThing([property: Required, StringLength(50)] string Name, [property: Range(0, 1000)] int Weight);

    public sealed record GetThing(string Id);

    public sealed class ThingStore
    {
        private readonly Thing _thing = new("42", "anvil", 1);
        public Thing Get(string id) => _thing with { Id = id };
        public Thing Create(string name) => _thing with { Name = name };
    }

    [Get("things/{id}"), AllowAnonymous]
    public sealed class GetThingEndpoint(ThingStore store) : ResultEndpoint<GetThing, Microsoft.AspNetCore.Http.HttpResults.Ok<Thing>>
    {
        public ThingStore Store => store;
        public override Task<Microsoft.AspNetCore.Http.HttpResults.Ok<Thing>> HandleAsync(GetThing request, CancellationToken ct) => Task.FromResult(TypedResults.Ok(store.Get(request.Id)));
    }

    [Post("things"), AllowAnonymous]
    public sealed class CreateThingEndpoint(ThingStore store)
        : ResultEndpoint<CreateThing, Microsoft.AspNetCore.Http.HttpResults.Created<Thing>>
    {
        public override Task<Microsoft.AspNetCore.Http.HttpResults.Created<Thing>> HandleAsync(CreateThing request, CancellationToken ct) =>
            Task.FromResult(TypedResults.Created($"/things/{request.Name}", store.Create(request.Name)));
    }

    public sealed class BodyDetection : Microsoft.AspNetCore.Http.Features.IHttpRequestBodyDetectionFeature
    {
        public static readonly BodyDetection Instance = new();
        public bool CanHaveBody => true;
    }

    public sealed record Scenario(string Name, string Method, string Route, Dictionary<string, string> RouteValues, string? Body);

    /// <summary>Calls one endpoint's request delegate directly, as routing would after matching.</summary>
    public sealed class Invoker(RouteEndpoint endpoint, Scenario scenario, IServiceScopeFactory scopes, string prefix)
    {
        private readonly byte[] _body = scenario.Body is null ? [] : Encoding.UTF8.GetBytes(scenario.Body);
        private readonly MemoryStream _response = new();

        public static Invoker Create(WebApplication app, Scenario scenario, string prefix)
        {
            var pattern = $"/{prefix}/{scenario.Route}";
            // The application's own data sources: without a started server, the global one is not wired up yet.
            var endpoint = ((IEndpointRouteBuilder)app).DataSources.SelectMany(d => d.Endpoints)
                .OfType<RouteEndpoint>()
                .Single(e => string.Equals(e.RoutePattern.RawText?.TrimEnd('/'), pattern, StringComparison.OrdinalIgnoreCase)
                             && e.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods.Contains(scenario.Method) == true);
            return new Invoker(endpoint, scenario, app.Services.GetRequiredService<IServiceScopeFactory>(), prefix);
        }

        private async Task<(int Status, string Body)> InvokeAsync()
        {
            await using var scope = scopes.CreateAsyncScope();
            var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
            context.Request.Method = scenario.Method;
            context.Request.Path = $"/{prefix}/{scenario.Route}";
            foreach (var (key, value) in scenario.RouteValues) context.Request.RouteValues[key] = value;
            if (scenario.Body is not null)
            {
                context.Features.Set<Microsoft.AspNetCore.Http.Features.IHttpRequestBodyDetectionFeature>(BodyDetection.Instance);
                context.Request.ContentType = "application/json";
                context.Request.ContentLength = _body.Length;
                context.Request.Body = new MemoryStream(_body, writable: false);
            }

            _response.SetLength(0);
            context.Response.Body = _response;
            context.SetEndpoint(endpoint);
            await endpoint.RequestDelegate!(context);
            return (context.Response.StatusCode, Encoding.UTF8.GetString(_response.GetBuffer(), 0, (int)_response.Length));
        }

        public async Task VerifyAsync()
        {
            var (status, body) = await InvokeAsync();
            Console.Error.WriteLine($"  check {prefix,-10} {scenario.Method} {scenario.Route}: {status} {body}");
            if (status is < 200 or > 299) throw new InvalidOperationException($"{prefix} {scenario.Route} answered {status}: {body}");
        }

        /// <summary>Runs <paramref name="count"/> requests; returns nanoseconds per request.</summary>
        public async Task<double> RunAsync(int count)
        {
            var clock = Stopwatch.StartNew();
            for (var i = 0; i < count; i++) await InvokeAsync();
            return clock.Elapsed.TotalNanoseconds / count;
        }

        public async Task<long> AllocatedBytesPerRequestAsync()
        {
            const int count = 2_000;
            await RunAsync(count);
            var before = GC.GetTotalAllocatedBytes(precise: true);
            await RunAsync(count);
            return (GC.GetTotalAllocatedBytes(precise: true) - before) / count;
        }
    }
}
