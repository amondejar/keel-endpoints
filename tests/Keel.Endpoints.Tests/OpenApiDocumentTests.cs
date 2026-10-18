#if KEEL_OPENAPI
using System.Text.Json;
using Keel.Sample;
using static Keel.Endpoints.Tests.Harness;

namespace Keel.Endpoints.Tests;

/// <summary>
/// Generates the real OpenAPI document with Microsoft.AspNetCore.OpenApi and checks it.
/// Compiled only when that package is available (normal builds and CI, not offline builds).
/// </summary>
internal static class OpenApiDocumentTests
{
    public static void Register()
    {
        Test("OpenAPI document: operations, tags, parameters and responses", async () =>
        {
            // The sample registers OpenAPI and Scalar itself, in Development.
            await using var app = SampleApp.Create(["--urls", "http://127.0.0.1:0", "--environment", "Development",
                "--Logging:LogLevel:Default=Warning"]);
            await app.StartAsync();

            using var http = new HttpClient { BaseAddress = new Uri(app.Urls.First()) };
            using var document = JsonDocument.Parse(await http.GetStringAsync("/openapi/v1.json"));
            var paths = document.RootElement.GetProperty("paths");

            var get = paths.GetProperty("/api/invoices/{invoiceId}").GetProperty("get");
            Assert.Equal("InvoicesGet", get.GetProperty("operationId").GetString());
            Assert.Equal("Invoices", get.GetProperty("tags")[0].GetString());
            Assert.Equal("path", get.GetProperty("parameters")[0].GetProperty("in").GetString());
            Assert.True(get.GetProperty("responses").TryGetProperty("200", out _));
            Assert.True(get.GetProperty("responses").TryGetProperty("404", out _));

            var create = paths.GetProperty("/api/invoices").GetProperty("post");
            Assert.True(create.TryGetProperty("requestBody", out _));
            Assert.True(create.GetProperty("responses").TryGetProperty("201", out _));
            Assert.True(create.GetProperty("responses").TryGetProperty("400", out _));

            var list = paths.GetProperty("/api/invoices").GetProperty("get");
            Assert.Equal("query", list.GetProperty("parameters")[0].GetProperty("in").GetString());

            // The X-Api-Key scheme is declared, so Scalar can authenticate requests.
            var scheme = document.RootElement.GetProperty("components").GetProperty("securitySchemes").GetProperty("ApiKey");
            Assert.Equal("apiKey", scheme.GetProperty("type").GetString());
            Assert.Equal("X-Api-Key", scheme.GetProperty("name").GetString());

            // And the Scalar page is served.
            var scalar = await http.GetAsync("/scalar");
            Assert.Equal(System.Net.HttpStatusCode.OK, scalar.StatusCode);

            await app.StopAsync();
        });
    }
}
#endif
