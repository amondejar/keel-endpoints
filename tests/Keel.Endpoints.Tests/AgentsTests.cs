using System.Text.Json;
using Keel.Endpoints.Agents;
using Keel.Endpoints.Agents.Internal;
using Keel.Endpoints.Mcp;
using Keel.Endpoints.Tests.AgentsFixtures.Payments;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using static Keel.Endpoints.Tests.Harness;

namespace Keel.Endpoints.Tests;

/// <summary>Keel.Endpoints.Agents: the user confirms through the MCP client before a guarded tool runs.</summary>
internal static class AgentsTests
{
    public static void Register()
    {
        Test("Agents: the server asks the user, with the call's data, and runs the tool once confirmed", async () =>
        {
            await using var app = await StartAsync();
            var asked = new List<string>();
            await using var client = await Client(app, request =>
            {
                asked.Add(request.Message);
                return Answer("accept", confirm: true);
            });

            var result = await client.CallToolAsync("payments_refund", new Dictionary<string, object?> { ["id"] = 7, ["amount"] = 12.5 });

            Assert.True(result.IsError is null or false, Text(result));
            Assert.Equal("Refund payment 7: 12.5 EUR", string.Join("|", asked));
            Assert.Equal("refund 7 12.5", string.Join("|", Ledger(app).Actions));
            var audit = Audit(app);
            Assert.Equal("ConfirmationRequested,Confirmed", string.Join(",", audit.Select(e => e.Decision)));
            Assert.Equal(true, audit[^1].Succeeded);
            Assert.Equal(audit[0].ArgumentsHash, audit[1].ArgumentsHash);
        });

        Test("Agents: declined, dismissed or unticked, the tool does not run, and the agent is told which", async () =>
        {
            foreach (var (action, confirm, said) in new[]
            {
                ("decline", (bool?)null, "user declined"),
                ("cancel", null, "dismissed"),
                ("accept", false, "unticked Confirm"),
            })
            {
                await using var app = await StartAsync();
                await using var client = await Client(app, _ => Answer(action, confirm));

                var result = await client.CallToolAsync("payments_refund", new Dictionary<string, object?> { ["id"] = 7, ["amount"] = 12.5 });

                Assert.Equal(true, result.IsError);
                Assert.True(Text(result).Contains(said), $"{action}/{confirm}: {Text(result)}");
                Assert.Equal(0, Ledger(app).Actions.Count);
                Assert.Equal(AgentDecision.Declined, Audit(app)[^1].Decision);
            }
        });

        Test("Agents: accepting confirms; the Confirm box starts ticked", async () =>
        {
            foreach (var confirm in new bool?[] { true, null })
            {
                await using var app = await StartAsync();
                ElicitRequestParams? asked = null;
                await using var client = await Client(app, request =>
                {
                    asked = request;
                    return Answer("accept", confirm);
                });

                var result = await client.CallToolAsync("payments_refund", new Dictionary<string, object?> { ["id"] = 7, ["amount"] = 12.5 });

                Assert.True(result.IsError is null or false, $"{confirm}: {Text(result)}");
                Assert.Equal("refund 7 12.5", string.Join("|", Ledger(app).Actions));
                var box = (ElicitRequestParams.BooleanSchema)asked!.RequestedSchema!.Properties["confirm"];
                Assert.Equal(true, box.Default);
            }
        });

        Test("Agents: destructive tools ask by default; WithoutConfirmation and additive tools do not", async () =>
        {
            await using var app = await StartAsync();
            var asked = new List<string>();
            await using var client = await Client(app, request =>
            {
                asked.Add(request.Message);
                return Answer("accept", confirm: true);
            });

            await client.CallToolAsync("payments_cancel", new Dictionary<string, object?> { ["id"] = 1 });
            await client.CallToolAsync("payments_archive", new Dictionary<string, object?> { ["id"] = 2 });
            await client.CallToolAsync("payments_note", new Dictionary<string, object?> { ["id"] = 3, ["text"] = "hi" });
            await client.CallToolAsync("payments_list");

            Assert.Equal(1, asked.Count);
            Assert.True(asked[0].Contains("payments_cancel") && asked[0].Contains("Cancels a payment for good."), asked[0]);
            Assert.Equal("cancel 1|archive 2|note 3", string.Join("|", Ledger(app).Actions));
            // Reads are not audited; every write is.
            Assert.Equal(
                "payments_cancel:ConfirmationRequested,payments_cancel:Confirmed,payments_archive:NotRequired,payments_note:NotRequired",
                string.Join(",", Audit(app).Select(e => $"{e.Tool}:{e.Decision}")));
        });

        Test("Agents: a client that cannot ask the user gets a refusal, and nothing runs", async () =>
        {
            await using var app = await StartAsync();
            // An older protocol revision: no multi round-trip requests, so no way to ask.
            await using var client = await Client(app, _ => Answer("accept", confirm: true), protocolVersion: "2025-11-25");

            var result = await client.CallToolAsync("payments_cancel", new Dictionary<string, object?> { ["id"] = 1 });

            Assert.Equal(true, result.IsError);
            Assert.True(Text(result).Contains("cannot ask"), Text(result));
            Assert.Equal(0, Ledger(app).Actions.Count);
            Assert.Equal(AgentDecision.Refused, Audit(app)[^1].Decision);
        });

        Test("Agents: a client without elicitation does not get the tool run", async () =>
        {
            await using var app = await StartAsync();
            await using var client = await Client(app, answer: null);

            try
            {
                var result = await client.CallToolAsync("payments_cancel", new Dictionary<string, object?> { ["id"] = 1 });
                Assert.Equal(true, result.IsError);
            }
            catch (Exception ex) when (ex is not AssertionException)
            {
                // The client could not answer the elicitation: also fine.
            }

            Assert.Equal(0, Ledger(app).Actions.Count);
        });

        Test("Agents: with ConfirmDestructiveTools = false, destructive tools run without asking", async () =>
        {
            await using var app = await StartAsync(o => o.ConfirmDestructiveTools = false);
            await using var client = await Client(app, _ => throw new InvalidOperationException("should not ask"));

            var result = await client.CallToolAsync("payments_cancel", new Dictionary<string, object?> { ["id"] = 1 });

            Assert.True(result.IsError is null or false, Text(result));
            Assert.Equal("cancel 1", string.Join("|", Ledger(app).Actions));
        });

        Test("Agents: a confirmation is bound to the tool, the arguments and the user, and is tamper-proof", () =>
        {
            var tokens = new ConfirmationTokens(new EphemeralDataProtectionProvider(), Options.Create(new KeelAgentsOptions()), TimeProvider.System);
            var token = tokens.Create("payments_refund", "h1", "ana");

            Assert.True(tokens.IsValid(token, "payments_refund", "h1", "ana"));
            Assert.True(!tokens.IsValid(token, "payments_refund", "h2", "ana"));
            Assert.True(!tokens.IsValid(token, "payments_cancel", "h1", "ana"));
            Assert.True(!tokens.IsValid(token, "payments_refund", "h1", "eve"));
            Assert.True(!tokens.IsValid(token, "payments_refund", "h1", null));
            Assert.True(!tokens.IsValid(token[..^2] + "AA", "payments_refund", "h1", "ana"));
            Assert.True(!tokens.IsValid("not a token", "payments_refund", "h1", "ana"));

            var expired = new ConfirmationTokens(new EphemeralDataProtectionProvider(), Options.Create(new KeelAgentsOptions { ConfirmationLifetime = TimeSpan.FromSeconds(-1) }), TimeProvider.System);
            Assert.True(!expired.IsValid(expired.Create("t", "h", "u"), "t", "h", "u"));
        });

        Test("Agents: the arguments hash ignores key order", () =>
        {
            static IDictionary<string, JsonElement> Args(string json) =>
                JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)!;

            Assert.Equal(
                ConfirmationTokens.Hash(Args("""{ "a": 1, "b": { "x": true, "y": [1, 2] } }""")),
                ConfirmationTokens.Hash(Args("""{ "b": { "y": [1, 2], "x": true }, "a": 1 }""")));
            Assert.True(ConfirmationTokens.Hash(Args("""{ "a": 1 }""")) != ConfirmationTokens.Hash(Args("""{ "a": 2 }""")));
        });
    }

    private static ValueTask<ElicitResult> Answer(string action, bool? confirm) => ValueTask.FromResult(new ElicitResult
    {
        Action = action,
        Content = confirm is { } value ? new Dictionary<string, JsonElement> { ["confirm"] = JsonSerializer.SerializeToElement(value) } : null,
    });

    private static string Text(CallToolResult result) => string.Concat(result.Content.OfType<TextContentBlock>().Select(c => c.Text));

    private static PaymentLedger Ledger(WebApplication app) => app.Services.GetRequiredService<PaymentLedger>();

    private static IReadOnlyList<AgentAuditEntry> Audit(WebApplication app) => app.Services.GetRequiredService<RecordingAuditSink>().Entries;

    private static async Task<WebApplication> StartAsync(Action<KeelAgentsOptions>? configure = null)
    {
        var builder = WebApplication.CreateBuilder(["--urls", "http://127.0.0.1:0", "--Logging:LogLevel:Default=None"]);
        builder.Services.AddKeelEndpoints();
        builder.Services.AddTestValidation();
        builder.Services.AddProblemDetails();
        builder.Services.AddSingleton<PaymentLedger>();
        builder.Services.AddSingleton<RecordingAuditSink>();
        builder.Services.AddSingleton<IAgentAuditSink>(s => s.GetRequiredService<RecordingAuditSink>());
        builder.Services.AddKeelMcp();
        builder.Services.AddKeelAgents(configure);
        var app = builder.Build();

        var api = app.MapGroup("/api");
        api.MapEndpoint<AgentsFixtures.Payments.Refund.Endpoint>();
        api.MapEndpoint<AgentsFixtures.Payments.Cancel.Endpoint>();
        api.MapEndpoint<AgentsFixtures.Payments.Archive.Endpoint>();
        api.MapEndpoint<AgentsFixtures.Payments.Note.Endpoint>();
        api.MapEndpoint<AgentsFixtures.Payments.List.Endpoint>();
        try
        {
            app.MapKeelMcp();
            await app.StartAsync();
        }
        catch
        {
            await app.DisposeAsync();
            throw;
        }

        return app;
    }

    private static Task<McpClient> Client(WebApplication app, Func<ElicitRequestParams, ValueTask<ElicitResult>>? answer, string? protocolVersion = null)
    {
        var options = new McpClientOptions { ProtocolVersion = protocolVersion };
        if (answer is not null) options.Handlers.ElicitationHandler = (request, _) => answer(request!);
        return McpClient.CreateAsync(
            new HttpClientTransport(new HttpClientTransportOptions { Endpoint = new Uri(app.Urls.First() + "/mcp") }),
            options);
    }
}
