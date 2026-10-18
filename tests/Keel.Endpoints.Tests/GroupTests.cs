using System.Net;
using Keel.Endpoints.Internal;
using Microsoft.AspNetCore.Http.Metadata;
using static Keel.Endpoints.Tests.Harness;

namespace Keel.Endpoints.Tests;

internal static class GroupTests
{
    public static void Register()
    {
        Test("Groups: a group covers its namespace and the ones below it, not lookalike prefixes", () =>
        {
            Assert.True(EndpointGroups.Covers("Shop", "Shop"));
            Assert.True(EndpointGroups.Covers("Shop", "Shop.Admin.Get"));
            Assert.True(EndpointGroups.Covers("", "Anything.At.All"));
            Assert.True(!EndpointGroups.Covers("Shop", "Shopping"));
            Assert.True(!EndpointGroups.Covers("Shop.Admin", "Shop"));
        });

        Test("Groups: outermost group first, then inner groups, then the endpoint's Configure", async () =>
        {
            await using var app = await StartAsync(a => a.MapEndpoint<Groups.Shop.Admin.Endpoint>());
            var response = await Client(app).GetAsync("/shop/admin/ping");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("shop,admin,endpoint", string.Join(",", response.Headers.GetValues("X-Order")));
        });

        Test("Groups: a group's AllowAnonymous satisfies fail-closed and its tags reach OpenAPI", async () =>
        {
            await using var app = await StartAsync(a => a.MapEndpoint<Groups.Shop.Admin.Endpoint>());
            var endpoint = app.Services.GetRequiredService<EndpointDataSource>().Endpoints
                .Single(e => e.Metadata.GetMetadata<KeelEndpointMetadata>()?.EndpointType == typeof(Groups.Shop.Admin.Endpoint));
            Assert.True(endpoint.Metadata.OfType<ITagsMetadata>().SelectMany(t => t.Tags).Contains("Admin"));
            Assert.True(endpoint.Metadata.GetMetadata<Microsoft.AspNetCore.Authorization.IAllowAnonymous>() is not null);
        });

        Test("Groups: an endpoint outside every group is still fail-closed", async () =>
        {
            var app = Build();
            app.MapEndpoint<Groups.Shopping.Endpoint>();
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => app.StartAsync());
            Assert.True(error.Message.Contains("no authorization decision"));
            await app.DisposeAsync();
        });

        Test("Groups: a group that sets Binding is rejected at map time", async () =>
        {
            await using var app = Build();
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            {
                app.MapEndpoint<Groups.Misconfigured.Endpoint>();
                return Task.CompletedTask;
            });
            Assert.True(error.Message.Contains(nameof(Groups.Misconfigured.BindingGroup)) && error.Message.Contains("belong to endpoints"));
        });

        Test("Groups: a group without a public parameterless constructor is rejected", async () =>
        {
            await using var app = Build();
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            {
                app.MapEndpoint<Groups.WithDependencies.Endpoint>();
                return Task.CompletedTask;
            });
            Assert.True(error.Message.Contains("public parameterless constructor"));
        });

        Test("Groups: the sample's InvoicesGroup applies through the generated registration", async () =>
        {
            await using var host = await SampleHost.StartAsync();
            var response = await host.Client().SendAsync<Sample.Invoices.List.Endpoint>(new Sample.Invoices.ListInvoices(null));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("invoices", response.Headers.GetValues("X-Feature").Single());

            var health = await host.Client().SendAsync<Sample.Health.Endpoint>();
            Assert.True(!health.Headers.Contains("X-Feature"));
        });
    }

    private static WebApplication Build()
    {
        var builder = WebApplication.CreateBuilder(["--urls", "http://127.0.0.1:0", "--Logging:LogLevel:Default=None"]);
        builder.Services.AddKeelEndpoints();
        builder.Services.AddAuthorization();
        return builder.Build();
    }

    private static async Task<WebApplication> StartAsync(Action<WebApplication> map)
    {
        var app = Build();
        app.UseAuthorization();
        map(app);
        await app.StartAsync();
        return app;
    }

    private static HttpClient Client(WebApplication app) => new() { BaseAddress = new Uri(app.Urls.First()) };
}
