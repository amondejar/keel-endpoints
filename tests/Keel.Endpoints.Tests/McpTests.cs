using System.Security.Claims;
using System.Text.Json;
using Keel.Endpoints.Mcp;
using Keel.Endpoints.Mcp.Internal;
using Keel.Endpoints.Tests.McpFixtures.Invoices;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using static Keel.Endpoints.Tests.Harness;

namespace Keel.Endpoints.Tests;

/// <summary>Keel.Endpoints.Mcp: endpoints as MCP tools, called by the official MCP client over Streamable HTTP.</summary>
internal static class McpTests
{
    public static void Register()
    {
        Test("MCP: only endpoints with ExposeAsTool are tools, named after the operation", async () =>
        {
            await using var app = await StartAsync();
            await using var client = await Client(app);

            var names = (await client.ListToolsAsync()).Select(t => t.Name).Order(StringComparer.Ordinal).ToArray();

            // Anonymous: create and delete need a user, so they are not listed; the PDF endpoint is not exposed.
            Assert.Equal("invoices_boom,invoices_get,invoices_search", string.Join(",", names));
        });

        Test("MCP: a caller sees the tools its identity may call", async () =>
        {
            await using var app = await StartAsync();
            await using var user = await Client(app, user: "ana");
            await using var admin = await Client(app, user: "root", role: "admin");

            var forUser = (await user.ListToolsAsync()).Select(t => t.Name).ToHashSet();
            var forAdmin = (await admin.ListToolsAsync()).Select(t => t.Name).ToHashSet();

            Assert.True(forUser.Contains("invoices_create") && !forUser.Contains("invoices_delete"));
            Assert.True(forAdmin.Contains("invoices_delete"));
        });

        Test("MCP: the input schema is the contract, with its DataAnnotations rules", async () =>
        {
            await using var app = await StartAsync();
            await using var client = await Client(app, user: "ana");

            var tools = await client.ListToolsAsync();
            var create = tools.Single(t => t.Name == "invoices_create").ProtocolTool;
            var schema = create.InputSchema;
            var customer = schema.GetProperty("properties").GetProperty("customer");
            var amount = schema.GetProperty("properties").GetProperty("amount");

            Assert.Equal("object", schema.GetProperty("type").GetString());
            Assert.Equal(40, customer.GetProperty("maxLength").GetInt32());
            Assert.Equal("Who the invoice is for.", customer.GetProperty("description").GetString());
            Assert.Equal(1d, amount.GetProperty("minimum").GetDouble());
            Assert.Equal(10000d, amount.GetProperty("maximum").GetDouble());
            Assert.True(schema.GetProperty("required").EnumerateArray().Any(r => r.GetString() == "customer"));
            Assert.Equal("Creates an invoice for a customer.", create.Description);

            var get = tools.Single(t => t.Name == "invoices_get").ProtocolTool;
            Assert.True(get.InputSchema.GetProperty("required").EnumerateArray().Any(r => r.GetString() == "id"));
            Assert.True(get.OutputSchema is { } output && output.GetProperty("properties").TryGetProperty("customer", out _));
        });

        Test("MCP: hints come from the method; writes say whether they are destructive", async () =>
        {
            await using var app = await StartAsync();
            await using var client = await Client(app, user: "root", role: "admin");

            var tools = (await client.ListToolsAsync()).ToDictionary(t => t.Name, t => t.ProtocolTool.Annotations!);

            Assert.Equal(true, tools["invoices_get"].ReadOnlyHint);
            Assert.Equal(false, tools["invoices_create"].ReadOnlyHint);
            Assert.Equal(false, tools["invoices_create"].DestructiveHint);
            Assert.Equal(false, tools["invoices_create"].IdempotentHint);
            Assert.Equal(true, tools["invoices_delete"].DestructiveHint);
            Assert.Equal(true, tools["invoices_delete"].IdempotentHint);
            Assert.Equal(false, tools["invoices_get"].OpenWorldHint);
        });

        Test("MCP: a call runs the endpoint and returns its body, also as structured content", async () =>
        {
            await using var app = await StartAsync();
            await using var client = await Client(app);

            var result = await client.CallToolAsync("invoices_get", new Dictionary<string, object?> { ["id"] = 1 });

            Assert.True(result.IsError is null or false);
            Assert.True(Text(result).Contains("\"customer\":\"Acme\""));
            Assert.Equal("Acme", result.StructuredContent!.Value.GetProperty("customer").GetString());
        });

        Test("MCP: query parameters of a GET contract come from the arguments", async () =>
        {
            await using var app = await StartAsync();
            await using var client = await Client(app);

            var result = await client.CallToolAsync("invoices_search", new Dictionary<string, object?> { ["customer"] = "Globex" });

            using var invoices = JsonDocument.Parse(Text(result));
            Assert.Equal(1, invoices.RootElement.GetArrayLength());
            Assert.Equal(2, invoices.RootElement[0].GetProperty("id").GetInt32());
        });

        Test("MCP: the endpoint's errors reach the agent as tool errors (404, validation 400)", async () =>
        {
            await using var app = await StartAsync();
            await using var client = await Client(app, user: "ana");

            var missing = await client.CallToolAsync("invoices_get", new Dictionary<string, object?> { ["id"] = 99 });
            var invalid = await client.CallToolAsync("invoices_create", new Dictionary<string, object?> { ["customer"] = "Acme", ["amount"] = 0 });

            Assert.Equal(true, missing.IsError);
            Assert.True(Text(missing).StartsWith("404", StringComparison.Ordinal));
            Assert.Equal(true, invalid.IsError);
            Assert.True(Text(invalid).StartsWith("400", StringComparison.Ordinal) && Text(invalid).Contains("Amount", StringComparison.OrdinalIgnoreCase));
            Assert.Equal(2, app.Services.GetRequiredService<InvoiceStore>().All.Count);
        });

        Test("MCP: a write runs as the calling user, and only if its policies allow it", async () =>
        {
            await using var app = await StartAsync();
            await using var anonymous = await Client(app);
            await using var user = await Client(app, user: "ana");
            await using var admin = await Client(app, user: "root", role: "admin");

            var unauthenticated = await anonymous.CallToolAsync("invoices_create", new Dictionary<string, object?> { ["customer"] = "Initech", ["amount"] = 10 });
            var forbidden = await user.CallToolAsync("invoices_delete", new Dictionary<string, object?> { ["id"] = 1 });
            var created = await user.CallToolAsync("invoices_create", new Dictionary<string, object?> { ["customer"] = "Initech", ["amount"] = 10 });
            var deleted = await admin.CallToolAsync("invoices_delete", new Dictionary<string, object?> { ["id"] = 2 });

            var store = app.Services.GetRequiredService<InvoiceStore>();
            Assert.True(Text(unauthenticated).StartsWith("401", StringComparison.Ordinal));
            Assert.True(Text(forbidden).StartsWith("403", StringComparison.Ordinal));
            Assert.True(created.IsError is null or false);
            Assert.Equal("Initech", created.StructuredContent!.Value.GetProperty("customer").GetString());
            Assert.True(deleted.IsError is null or false && Text(deleted).StartsWith("204", StringComparison.Ordinal));
            Assert.True(store.Find(1) is not null && store.Find(2) is null && store.All.Any(i => i.Customer == "Initech"));
        });

        Test("MCP: an unhandled exception is a generic tool error, without its message", async () =>
        {
            await using var app = await StartAsync();
            await using var client = await Client(app);

            var result = await client.CallToolAsync("invoices_boom");

            Assert.Equal(true, result.IsError);
            Assert.True(Text(result).StartsWith("500", StringComparison.Ordinal) && !Text(result).Contains("secret"));
        });

        Test("MCP: an unknown tool is a protocol error", async () =>
        {
            await using var app = await StartAsync();
            await using var client = await Client(app);

            await Assert.ThrowsAsync<McpException>(async () => await client.CallToolAsync("invoices_nope"));
        });

        Test("MCP: a write tool that does not say whether it is destructive stops the host", async () =>
        {
            var error = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                await using var app = await StartAsync(api => api.MapEndpoint<McpFixtures.Broken.Write.Endpoint>());
            });

            Assert.True(error.Message.Contains("must say whether it is destructive") && error.Message.Contains("POST"));
        });

        Test("MCP: two tools with the same name stop the host", async () =>
        {
            var error = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                await using var app = await StartAsync(api =>
                {
                    api.MapEndpoint<McpFixtures.Broken.Same.One>();
                    api.MapEndpoint<McpFixtures.Broken.Same.Two>();
                });
            });

            Assert.True(error.Message.Contains("are both the MCP tool 'same'"));
        });

        Test("MCP: tool names are the operation names in snake case", () =>
        {
            Assert.Equal("invoices_get", ToolNames.SnakeCase("InvoicesGet"));
            Assert.Equal("orders_get_v2", ToolNames.SnakeCase("OrdersGetV2"));
            Assert.Equal("api_key_rotate", ToolNames.SnakeCase("APIKeyRotate"));
            Assert.Equal("health", ToolNames.SnakeCase("Health"));
            Assert.True(ToolNames.IsValid("invoices.get-v2") && !ToolNames.IsValid("invoices get"));
        });
    }

    private static string Text(CallToolResult result) => string.Concat(result.Content.OfType<TextContentBlock>().Select(c => c.Text));

    private static async Task<WebApplication> StartAsync(Action<RouteGroupBuilder>? extra = null)
    {
        var builder = WebApplication.CreateBuilder(["--urls", "http://127.0.0.1:0", "--Logging:LogLevel:Default=None"]);
        builder.Services.AddKeelEndpoints();
        builder.Services.AddTestValidation();
        builder.Services.AddProblemDetails();
        builder.Services.AddAuthorization(o => o.AddPolicy("admin", p => p.RequireRole("admin")));
        builder.Services.AddSingleton<InvoiceStore>();
        builder.Services.AddKeelMcp();
        var app = builder.Build();

        // A stand-in for authentication: X-User becomes the caller, X-Role its role.
        app.Use((context, next) =>
        {
            if (context.Request.Headers["X-User"].ToString() is { Length: > 0 } name)
            {
                var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, name), new(ClaimTypes.Name, name) };
                if (context.Request.Headers["X-Role"].ToString() is { Length: > 0 } role) claims.Add(new(ClaimTypes.Role, role));
                context.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
            }

            return next(context);
        });

        var api = app.MapGroup("/api");
        if (extra is null)
        {
            api.MapEndpoint<McpFixtures.Invoices.Boom.Endpoint>();
            api.MapEndpoint<McpFixtures.Invoices.Get.Endpoint>();
            api.MapEndpoint<McpFixtures.Invoices.Search.Endpoint>();
            api.MapEndpoint<McpFixtures.Invoices.Create.Endpoint>();
            api.MapEndpoint<McpFixtures.Invoices.Delete.Endpoint>();
            api.MapEndpoint<McpFixtures.Invoices.Pdf.Endpoint>();
        }
        else
        {
            extra(api);
        }

        app.MapKeelMcp();
        try
        {
            await app.StartAsync();
        }
        catch
        {
            await app.DisposeAsync();
            throw;
        }

        return app;
    }

    private static Task<McpClient> Client(WebApplication app, string? user = null, string? role = null)
    {
        var headers = new Dictionary<string, string>();
        if (user is not null) headers["X-User"] = user;
        if (role is not null) headers["X-Role"] = role;
        return McpClient.CreateAsync(new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = new Uri(app.Urls.First() + "/mcp"),
            AdditionalHeaders = headers,
        }));
    }
}
