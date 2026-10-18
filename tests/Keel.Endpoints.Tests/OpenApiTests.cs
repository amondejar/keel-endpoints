using FluentValidation;
using Keel.Endpoints.FluentValidation;
using Keel.Sample;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using static Keel.Endpoints.Tests.Harness;

namespace Keel.Endpoints.Tests;

/// <summary>
/// Checks what Microsoft.AspNetCore.OpenApi reads to build its document: the ApiExplorer descriptions.
/// These run without the OpenApi package; OpenApiDocumentTests checks the generated document itself.
/// </summary>
internal static class OpenApiTests
{
    public static void Register()
    {
        Test("OpenAPI: route, parameters and typed results are described", async () =>
        {
            var api = await DescribeSampleAsync();
            var get = api.Single(d => Name(d) == "InvoicesGet");

            Assert.Equal("api/invoices/{invoiceId}", get.RelativePath);
            Assert.Equal("Path", get.ParameterDescriptions.Single().Source.Id);
            Assert.True(get.SupportedResponseTypes.Any(r => r.StatusCode == 200 && r.Type?.Name == "InvoiceView"));
            Assert.True(get.SupportedResponseTypes.Any(r => r.StatusCode == 404));
            Assert.True(get.ParameterDescriptions.All(p => p.Type != typeof(HttpContext) && p.Type != typeof(CancellationToken)));
        });

        Test("OpenAPI: endpoints are tagged by feature, not by the internal handler type", async () =>
        {
            var api = await DescribeSampleAsync();
            Assert.Equal("Invoices", string.Join(",", Tags(api.Single(d => Name(d) == "InvoicesGet"))));
            Assert.Equal("Invoices", string.Join(",", Tags(api.Single(d => Name(d) == "InvoicesRename"))));
            Assert.True(api.All(d => Tags(d).Any() && !Tags(d).Contains("Handlers")));
        });

        Test("OpenAPI: an explicit tag wins over the namespace tag", async () =>
        {
            var api = await DescribeSampleAsync();
            // Create declares options.Tags("Invoices") itself: it must appear once, not twice.
            Assert.Equal("Invoices", string.Join(",", Tags(api.Single(d => Name(d) == "InvoicesCreate"))));
        });

        Test("OpenAPI: TagFromNamespace = false leaves endpoints untagged", async () =>
        {
            var api = await DescribeSampleAsync(b => b.Services.Configure<KeelOptions>(o => o.TagFromNamespace = false));
            Assert.Equal("", string.Join(",", Tags(api.Single(d => Name(d) == "InvoicesGet"))));
        });

        Test("OpenAPI: validatable contracts document the 400 validation problem", async () =>
        {
            var api = await DescribeSampleAsync();
            Assert.True(Has400(api.Single(d => Name(d) == "InvoicesCreate")));
            Assert.True(Has400(api.Single(d => Name(d) == "InvoicesRename")));  // via its [FromBody] member
            Assert.True(!Has400(api.Single(d => Name(d) == "InvoicesGet")));     // nothing to validate
        });

        Test("OpenAPI: WithFluentValidation documents 400 only where a validator exists", async () =>
        {
            var builder = WebApplication.CreateBuilder(["--urls", "http://127.0.0.1:0", "--Logging:LogLevel:Default=Warning"]);
            builder.Services.AddKeelEndpoints();
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddKeelFluentValidation(typeof(OpenApiTests).Assembly);
            await using var app = builder.Build();
            var group = app.MapGroup("/api").AllowAnonymous().WithFluentValidation();
            group.MapEndpoint<PlaceOrderEndpoint>();
            group.MapEndpoint<PingEndpoint>();
            await app.StartAsync();

            var api = Describe(app);
            Assert.True(Has400(api.Single(d => Name(d) == "PlaceOrder")));
            Assert.True(!Has400(api.Single(d => Name(d) == "Ping")));
            await app.StopAsync();
        });
    }

    private static async Task<List<ApiDescription>> DescribeSampleAsync(Action<WebApplicationBuilder>? configure = null)
    {
        await using var app = SampleApp.Create(["--urls", "http://127.0.0.1:0", "--Logging:LogLevel:Default=Warning"], b =>
        {
            b.Services.AddEndpointsApiExplorer();
            configure?.Invoke(b);
        });
        await app.StartAsync();
        var descriptions = Describe(app);
        await app.StopAsync();
        return descriptions;
    }

    private static List<ApiDescription> Describe(WebApplication app) =>
        [.. app.Services.GetRequiredService<IApiDescriptionGroupCollectionProvider>().ApiDescriptionGroups.Items.SelectMany(g => g.Items)];

    private static string? Name(ApiDescription d) =>
        d.ActionDescriptor.EndpointMetadata.OfType<IEndpointNameMetadata>().FirstOrDefault()?.EndpointName;

    private static List<string> Tags(ApiDescription d) =>
        [.. d.ActionDescriptor.EndpointMetadata.OfType<ITagsMetadata>().SelectMany(t => t.Tags)];

    private static bool Has400(ApiDescription d) =>
        d.SupportedResponseTypes.Any(r => r.StatusCode == 400 && r.Type == typeof(HttpValidationProblemDetails));
}
