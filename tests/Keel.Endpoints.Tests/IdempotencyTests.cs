using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using Keel.Endpoints.Idempotency;
using Keel.Endpoints.Idempotency.Internal;
using Keel.Endpoints.Testing;
using Keel.Endpoints.Tests.IdempotencyFixtures.Payments;
using static Keel.Endpoints.Tests.Harness;

namespace Keel.Endpoints.Tests;

internal static class IdempotencyTests
{
    public static void Register()
    {
        Test("Idempotency: a retry with the same key gets the stored response; the handler runs once", async () =>
        {
            await using var app = await StartAsync();
            var first = await Post(app, "/api/payments", new { account = "acme", amount = 10 }, key: "k1");
            var retry = await Post(app, "/api/payments", new { account = "acme", amount = 10 }, key: "k1");

            Assert.Equal(HttpStatusCode.Created, first.StatusCode);
            Assert.Equal(HttpStatusCode.Created, retry.StatusCode);
            Assert.Equal("/payments/1", retry.Headers.Location?.ToString());
            Assert.Equal(await first.Content.ReadAsStringAsync(), await retry.Content.ReadAsStringAsync());
            Assert.Equal("application/json; charset=utf-8", retry.Content.Headers.ContentType?.ToString());
            Assert.True(!first.Headers.Contains("Idempotent-Replayed"));
            Assert.Equal("true", retry.Headers.GetValues("Idempotent-Replayed").Single());
            Assert.Equal(1, Runs(app));
        });

        Test("Idempotency: the same key with a different request is 422; member order does not matter", async () =>
        {
            await using var app = await StartAsync();
            await PostRaw(app, "/api/payments", """{"account":"acme","amount":10}""", key: "k2");
            var reordered = await PostRaw(app, "/api/payments", """{ "amount": 10, "account": "acme" }""", key: "k2");
            var different = await PostRaw(app, "/api/payments", """{"account":"acme","amount":99}""", key: "k2");

            Assert.Equal("true", reordered.Headers.GetValues("Idempotent-Replayed").Single());
            Assert.Equal(HttpStatusCode.UnprocessableEntity, different.StatusCode);
            Assert.True((await different.Content.ReadAsStringAsync()).Contains("different request"));
            Assert.Equal(1, Runs(app));
        });

        Test("Idempotency: requests that fail validation or answer 5xx do not use up the key", async () =>
        {
            await using var app = await StartAsync();
            Assert.Equal(HttpStatusCode.BadRequest, (await Post(app, "/api/payments", new { account = "", amount = 10 }, key: "k3")).StatusCode);

            Ledger(app).FailNext = true;
            Assert.Equal(HttpStatusCode.InternalServerError, (await Post(app, "/api/payments", new { account = "acme", amount = 10 }, key: "k3")).StatusCode);

            var retry = await Post(app, "/api/payments", new { account = "acme", amount = 10 }, key: "k3");
            Assert.Equal(HttpStatusCode.Created, retry.StatusCode);
            Assert.True(!retry.Headers.Contains("Idempotent-Replayed"));
            Assert.Equal(2, Runs(app));
        });

        Test("Idempotency: a retry while the first request still runs gets 409, then the stored response", async () =>
        {
            await using var app = await StartAsync();
            var ledger = Ledger(app);
            ledger.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            var first = Post(app, "/api/payments", new { account = "acme", amount = 10 }, key: "k4");
            await ledger.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var concurrent = await Post(app, "/api/payments", new { account = "acme", amount = 10 }, key: "k4");
            Assert.Equal(HttpStatusCode.Conflict, concurrent.StatusCode);
            Assert.Equal("1", concurrent.Headers.GetValues("Retry-After").Single());

            ledger.Gate.SetResult();
            Assert.Equal(HttpStatusCode.Created, (await first).StatusCode);
            var after = await Post(app, "/api/payments", new { account = "acme", amount = 10 }, key: "k4");
            Assert.Equal("true", after.Headers.GetValues("Idempotent-Replayed").Single());
            Assert.Equal(1, ledger.Runs);
        });

        Test("Idempotency: without a key the request runs every time; RequireKey answers 400", async () =>
        {
            await using var app = await StartAsync();
            await Post(app, "/api/payments", new { account = "acme", amount = 10 }, key: null);
            await Post(app, "/api/payments", new { account = "acme", amount = 10 }, key: null);
            Assert.Equal(2, Runs(app));

            var missing = await Send(app, HttpMethod.Patch, "/api/payments/p1", null, key: null);
            Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
            Assert.True((await missing.Content.ReadAsStringAsync()).Contains("Idempotency-Key header is required"));
            Assert.Equal(HttpStatusCode.OK, (await Send(app, HttpMethod.Patch, "/api/payments/p1", null, key: "k5")).StatusCode);

            var tooLong = await Post(app, "/api/payments", new { account = "acme", amount = 10 }, key: new string('x', 300));
            Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
        });

        Test("Idempotency: keys are per user, so one caller never gets another's response", async () =>
        {
            await using var app = await StartAsync();
            var alice = await Post(app, "/api/payments", new { account = "acme", amount = 10 }, key: "shared", user: "alice");
            var bob = await Post(app, "/api/payments", new { account = "acme", amount = 10 }, key: "shared", user: "bob");
            Assert.True(!bob.Headers.Contains("Idempotent-Replayed"));
            Assert.True(await alice.Content.ReadAsStringAsync() != await bob.Content.ReadAsStringAsync());
            Assert.Equal(2, Runs(app));
        });

        Test("Idempotency: WithIdempotency() on a group covers its POSTs (plain results too) and leaves GETs alone", async () =>
        {
            await using var app = await StartAsync();
            var first = await Post(app, "/refunds/refunds", new { account = "acme", amount = 5 }, key: "r1");
            var retry = await Post(app, "/refunds/refunds", new { account = "acme", amount = 5 }, key: "r1");
            Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
            Assert.Equal(await first.Content.ReadAsStringAsync(), await retry.Content.ReadAsStringAsync());
            Assert.Equal("true", retry.Headers.GetValues("Idempotent-Replayed").Single());

            var get1 = await Send(app, HttpMethod.Get, "/refunds/refunds/9", null, key: "r1");
            var get2 = await Send(app, HttpMethod.Get, "/refunds/refunds/9", null, key: "r1");
            Assert.True(!get2.Headers.Contains("Idempotent-Replayed") && get1.StatusCode == HttpStatusCode.OK);
            Assert.Equal(3, Runs(app));
        });

        Test("Idempotency: the 409 and 422 (and 400 when required) responses are documented", async () =>
        {
            await using var app = await StartAsync();
            var manifest = ApiManifest.Create(app.Services);
            var create = manifest.Endpoints["POST api/payments"].Responses;
            Assert.True(create["409"] == "problem" && create["422"] == "problem");
            Assert.Equal("problem", manifest.Endpoints["PATCH api/payments/{id}"].Responses["400"]);
            Assert.True(!manifest.Endpoints["POST api/payments"].Responses.ContainsKey("400") || create["400"] == "validation-problem");
        });

        Test("Idempotency: stored responses round-trip through the cache format", () =>
        {
            var response = new IdempotentResponse
            {
                Fingerprint = "abc",
                StatusCode = 201,
                Headers = [new("Location", "/payments/1"), new("Content-Type", "application/json")],
                Body = Encoding.UTF8.GetBytes("""{"number":1}"""),
            };
            var copy = DistributedCacheIdempotencyStore.Read(DistributedCacheIdempotencyStore.Write(response));
            Assert.True(copy.Fingerprint == "abc" && copy.StatusCode == 201 && copy.Headers.Count == 2);
            Assert.Equal("""{"number":1}""", Encoding.UTF8.GetString(copy.Body.Span));
        });

        Test("Idempotency: options.Idempotent() without AddKeelIdempotency() names the missing call", async () =>
        {
            var builder = WebApplication.CreateBuilder(["--urls", "http://127.0.0.1:0", "--Logging:LogLevel:Default=None"]);
            builder.Services.AddKeelEndpoints();
            builder.Services.AddSingleton<Ledger>();
            await using var app = builder.Build();
            app.MapEndpoint<IdempotencyFixtures.Payments.Create.Endpoint>();
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => app.StartAsync());
            Assert.True(error.Message.Contains("AddKeelIdempotency()"));
        });
    }

    private static async Task<WebApplication> StartAsync()
    {
        var builder = WebApplication.CreateBuilder(["--urls", "http://127.0.0.1:0", "--Logging:LogLevel:Default=None"]);
        builder.Services.AddKeelEndpoints();
        builder.Services.AddTestValidation();
        builder.Services.AddProblemDetails();
        builder.Services.AddKeelIdempotency();
        builder.Services.AddSingleton<Ledger>();
        var app = builder.Build();

        // A stand-in for authentication: X-User becomes the caller.
        app.Use((context, next) =>
        {
            if (context.Request.Headers["X-User"].ToString() is { Length: > 0 } user)
                context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, user)], "test"));
            return next(context);
        });

        var api = app.MapGroup("/api");
        api.MapEndpoint<IdempotencyFixtures.Payments.Create.Endpoint>();
        api.MapEndpoint<IdempotencyFixtures.Payments.Capture.Endpoint>();
        var refunds = app.MapGroup("/refunds").WithIdempotency();
        refunds.MapEndpoint<IdempotencyFixtures.Refunds.Create.Endpoint>();
        refunds.MapEndpoint<IdempotencyFixtures.Refunds.Get.Endpoint>();

        await app.StartAsync();
        return app;
    }

    private static Ledger Ledger(WebApplication app) => app.Services.GetRequiredService<Ledger>();

    private static int Runs(WebApplication app) => Ledger(app).Runs;

    private static Task<HttpResponseMessage> Post(WebApplication app, string path, object body, string? key, string? user = null) =>
        Send(app, HttpMethod.Post, path, JsonContent.Create(body), key, user);

    private static Task<HttpResponseMessage> PostRaw(WebApplication app, string path, string json, string key) =>
        Send(app, HttpMethod.Post, path, new StringContent(json, Encoding.UTF8, "application/json"), key);

    private static async Task<HttpResponseMessage> Send(WebApplication app, HttpMethod method, string path, HttpContent? content, string? key, string? user = null)
    {
        using var http = new HttpClient { BaseAddress = new Uri(app.Urls.First()) };
        var request = new HttpRequestMessage(method, path) { Content = content };
        if (key is not null) request.Headers.Add("Idempotency-Key", key);
        if (user is not null) request.Headers.Add("X-User", user);
        var response = await http.SendAsync(request);
        await response.Content.LoadIntoBufferAsync();
        return response;
    }
}
