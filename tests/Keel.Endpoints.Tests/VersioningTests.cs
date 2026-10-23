using System.Net;
using Asp.Versioning;
using Keel.Endpoints.Testing;
using Keel.Endpoints.Versioning;
using static Keel.Endpoints.Tests.Harness;

namespace Keel.Endpoints.Tests;

internal static class VersioningTests
{
    public static void Register()
    {
        Test("Versioning: the folder gives the version, and each version answers on its own URL", async () =>
        {
            await using var app = await StartAsync(keelVersioning: true);
            var v1 = await Get(app, "/api/v1/orders/7");
            var v2 = await Get(app, "/api/v2/orders/7");

            Assert.Equal(HttpStatusCode.OK, v1.StatusCode);
            Assert.Equal("""{"id":"7","total":10}""", await v1.Content.ReadAsStringAsync());
            Assert.Equal("""{"id":"7","total":{"amount":10,"currency":"EUR"}}""", await v2.Content.ReadAsStringAsync());
            var supported = string.Join(",", v2.Headers.GetValues("api-supported-versions"));
            Assert.True(supported.Contains('1') && supported.Contains('2'));
            Assert.Equal(HttpStatusCode.OK, (await Get(app, "/api/v3/orders")).StatusCode);   // [ApiVersion(3.0)] on the class
        });

        Test("Versioning: operation names carry the version when an endpoint serves one", async () =>
        {
            await using var app = await StartAsync(keelVersioning: true);
            var manifest = ApiManifest.Create(app.Services);
            Assert.Equal("OrdersGetV1", manifest.Endpoints["GET api/v1/orders/{id}"].Operation);
            Assert.Equal("OrdersGetV2", manifest.Endpoints["GET api/v2/orders/{id}"].Operation);
            Assert.Equal("OrdersList", manifest.Endpoints["GET api/v3/orders"].Operation);            // two versions: unchanged
            Assert.True(manifest.Endpoints.Values.Any(e => e.Route.EndsWith("health", StringComparison.Ordinal) && e.Operation == "VersioningFixturesHealth")); // neutral

            // One manifest entry per version, so a change in v2 never hides a break in v1.
            Assert.Equal("OrderV1", manifest.Endpoints["GET api/v1/orders/{id}"].Responses["200"]);
            Assert.Equal("Money", manifest.Types["OrderV2"].Properties!["total"]);
        });

        Test("Versioning: without WithKeelVersioning() two versions of an operation share a name and routing fails", async () =>
        {
            await using var app = await StartAsync(keelVersioning: false);
            Assert.Equal(HttpStatusCode.InternalServerError, (await Get(app, "/api/v1/orders/7")).StatusCode);
        });

        Test("Versioning: version suffixes are identifier-safe", () =>
        {
            Assert.Equal("V2", KeelVersioningExtensions.Suffix(new ApiVersion(2, 0)));
            Assert.Equal("V2_1", KeelVersioningExtensions.Suffix(new ApiVersion(2, 1)));
            Assert.Equal("V2026_10_01", KeelVersioningExtensions.Suffix(new ApiVersion(new DateOnly(2026, 10, 1))));
            Assert.Equal("V2Beta", KeelVersioningExtensions.Suffix(new ApiVersion(2, 0, "beta")));
        });
    }

    private static async Task<WebApplication> StartAsync(bool keelVersioning)
    {
        var builder = WebApplication.CreateBuilder(["--urls", "http://127.0.0.1:0", "--Logging:LogLevel:Default=None"]);
        builder.Services.AddKeelEndpoints();
        builder.Services.AddApiVersioning(o => o.ReportApiVersions = true)
            .AddApiExplorer(o => { o.GroupNameFormat = "'v'V"; o.SubstituteApiVersionInUrl = true; });
        var app = builder.Build();

        var api = app.NewVersionedApi().MapGroup("/api/v{version:apiVersion}");
        if (keelVersioning) api.WithKeelVersioning();
        api.MapEndpoint<VersioningFixtures.V1.Orders.Get.Endpoint>();
        api.MapEndpoint<VersioningFixtures.V2.Orders.Get.Endpoint>();
        api.MapEndpoint<VersioningFixtures.V2.Orders.List.Endpoint>();
        api.MapEndpoint<VersioningFixtures.Health.Endpoint>();

        await app.StartAsync();
        return app;
    }

    private static async Task<HttpResponseMessage> Get(WebApplication app, string path)
    {
        using var http = new HttpClient { BaseAddress = new Uri(app.Urls.First()) };
        var response = await http.GetAsync(path);
        await response.Content.LoadIntoBufferAsync();
        return response;
    }
}
