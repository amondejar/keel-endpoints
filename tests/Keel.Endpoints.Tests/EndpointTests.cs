using System.Net;
using Keel.Sample.Health;
using Keel.Sample.Invoices;
using static Keel.Endpoints.Tests.Harness;

namespace Keel.Endpoints.Tests;

internal static class EndpointTests
{
    public static void Register()
    {
        Test("EndpointWithoutRequest + [AllowAnonymous] answers without credentials", async () =>
        {
            await using var host = await SampleHost.StartAsync();
            var response = await host.Client(apiKey: null).SendAsync<Sample.Health.Endpoint, HealthStatus>();
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("ok", response.Value?.Status);
        });

        Test("Group-level RequireAuthorization protects endpoints (401 without credentials)", async () =>
        {
            await using var host = await SampleHost.StartAsync();
            var response = await host.Client(apiKey: null).SendAsync<Sample.Invoices.List.Endpoint>(new ListInvoices(null));
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        });

        Test("POST binds the contract from the body and returns 201 with a typed result", async () =>
        {
            await using var host = await SampleHost.StartAsync();
            var created = await host.Client().SendAsync<Sample.Invoices.Create.Endpoint, InvoiceView>(new CreateInvoice("Acme", 120m));
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            Assert.Equal("Acme", created.Value?.Customer);
        });

        Test(".NET 10 native validation rejects an invalid contract with 400 problem details", async () =>
        {
            await using var host = await SampleHost.StartAsync();
            var response = await host.Client().SendAsync<Sample.Invoices.Create.Endpoint, InvoiceView>(new CreateInvoice("", 0m));
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.True(response.Body.Contains("Customer") && response.Body.Contains("Amount"));
        });

        Test("GET binds route values; Results<Ok, NotFound> picks the status", async () =>
        {
            await using var host = await SampleHost.StartAsync();
            var client = host.Client();
            var created = await client.SendAsync<Sample.Invoices.Create.Endpoint, InvoiceView>(new CreateInvoice("Acme", 10m));

            var found = await client.SendAsync<Sample.Invoices.Get.Endpoint, InvoiceView>(new GetInvoice(created.Value!.Id));
            Assert.Equal(HttpStatusCode.OK, found.StatusCode);
            Assert.Equal(created.Value.Id, found.Value?.Id);

            var missing = await client.SendAsync<Sample.Invoices.Get.Endpoint>(new GetInvoice("nope"));
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        });

        Test("GET binds the remaining contract members from the query string", async () =>
        {
            await using var host = await SampleHost.StartAsync();
            var client = host.Client();
            await client.SendAsync<Sample.Invoices.Create.Endpoint>(new CreateInvoice("Acme", 10m));

            var open = await client.SendAsync<Sample.Invoices.List.Endpoint, List<InvoiceView>>(new ListInvoices("open"));
            var paid = await client.SendAsync<Sample.Invoices.List.Endpoint, List<InvoiceView>>(new ListInvoices("paid"));
            Assert.Equal(1, open.Value?.Count);
            Assert.Equal(0, paid.Value?.Count);
        });

        Test("[BindRequest(Parameters)] combines a route value with a validated [FromBody] member", async () =>
        {
            await using var host = await SampleHost.StartAsync();
            var client = host.Client();
            var created = await client.SendAsync<Sample.Invoices.Create.Endpoint, InvoiceView>(new CreateInvoice("Acme", 10m));
            var id = created.Value!.Id;

            var renamed = await client.SendAsync<Sample.Invoices.Rename.Endpoint>(new RenameInvoice(id, new RenameInvoiceBody("Globex")));
            Assert.Equal(HttpStatusCode.NoContent, renamed.StatusCode);
            var after = await client.SendAsync<Sample.Invoices.Get.Endpoint, InvoiceView>(new GetInvoice(id));
            Assert.Equal("Globex", after.Value?.Customer);

            var invalid = await client.SendAsync<Sample.Invoices.Rename.Endpoint>(new RenameInvoice(id, new RenameInvoiceBody("")));
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        });

        Test("Endpoint<TRequest> returns 204; a policy in Configure is enforced (403 vs 204)", async () =>
        {
            await using var host = await SampleHost.StartAsync();
            var created = await host.Client().SendAsync<Sample.Invoices.Create.Endpoint, InvoiceView>(new CreateInvoice("Acme", 10m));

            var asUser = await host.Client("demo").SendAsync<Sample.Invoices.Delete.Endpoint>(new DeleteInvoice(created.Value!.Id));
            Assert.Equal(HttpStatusCode.Forbidden, asUser.StatusCode);

            var asAdmin = await host.Client("admin").SendAsync<Sample.Invoices.Delete.Endpoint>(new DeleteInvoice(created.Value.Id));
            Assert.Equal(HttpStatusCode.NoContent, asAdmin.StatusCode);
        });

        Test("StreamEndpoint writes Server-Sent Events with the configured event type", async () =>
        {
            await using var host = await SampleHost.StartAsync();
            using var response = await host.Client().SendAsync<Sample.Invoices.Watch.Endpoint>(new WatchInvoices(Count: 3));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);

            var body = await response.Content.ReadAsStringAsync();
            Assert.Equal(3, body.Split("event: invoice").Length - 1);
            Assert.True(body.Contains("\"invoiceId\":\"inv-3\""));
        });

        Test("Operation names come from the namespace of a class named Endpoint", () =>
        {
            Assert.Equal("InvoicesGet", KeelEndpointsExtensions.OperationName(typeof(Sample.Invoices.Get.Endpoint)));
            Assert.Equal("JsonStream", KeelEndpointsExtensions.OperationName(typeof(JsonStreamEndpoint)));
            return Task.CompletedTask;
        });

        Test("StreamEndpoint in its default format writes a JSON array", async () =>
        {
            var builder = WebApplication.CreateBuilder(["--urls", "http://127.0.0.1:0", "--Logging:LogLevel:Default=Warning"]);
            builder.Services.AddKeelEndpoints();
            await using var app = builder.Build();
            app.MapEndpoint<JsonStreamEndpoint>();
            await app.StartAsync();

            var address = app.Urls.First();
            using var http = new HttpClient { BaseAddress = new Uri(address) };
            var body = await http.GetStringAsync("/numbers?count=4");
            Assert.Equal("[1,2,3,4]", body);
            await app.StopAsync();
        });
    }
}

public sealed record CountRequest(int Count);

[Get("numbers")]
[Microsoft.AspNetCore.Authorization.AllowAnonymous]
public sealed class JsonStreamEndpoint : StreamEndpoint<CountRequest, int>
{
    public override async IAsyncEnumerable<int> HandleAsync(CountRequest request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        for (var i = 1; i <= request.Count; i++)
        {
            await Task.Yield();
            yield return i;
        }
    }
}
