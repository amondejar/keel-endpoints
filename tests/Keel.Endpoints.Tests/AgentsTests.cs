using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Keel.Endpoints.Agents;
using Keel.Endpoints.Agents.Internal;
using Keel.Endpoints.Idempotency;
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

        Test("Approvals: the agent gets a link; once someone else approves, its next identical call runs, once", async () =>
        {
            await using var app = await StartAsync();
            await using var client = await Client(app, answer: null, user: "ana");

            var first = await client.CallToolAsync("payments_payout", AcmePayout);
            var link = Link(first);
            Assert.True(first.IsError == true && Text(first).Contains("Approval required: nothing was done yet"), Text(first));
            Assert.True(Text(await client.CallToolAsync("payments_payout", AcmePayout)).Contains("still pending"));

            // The page shows what is asked, encoded, and cannot be framed.
            using (var http = new HttpClient())
            using (var page = await http.GetAsync(link))
            {
                var html = await page.Content.ReadAsStringAsync();
                Assert.Equal(200, (int)page.StatusCode);
                Assert.True(html.Contains("Pay 500 EUR to ACME &lt;now&gt;") && !html.Contains("<now>"), html);
                Assert.True(page.Headers.GetValues("Content-Security-Policy").Single().Contains("frame-ancestors 'none'"));
                Assert.Equal("no-referrer", page.Headers.GetValues("Referrer-Policy").Single());
            }

            Assert.Equal(403, (await Decide(link, approve: true, user: "ana")).Status);   // four eyes
            var approved = await Decide(link, approve: true, user: "bob");
            Assert.Equal(200, approved.Status);
            Assert.Equal("approved", Status(approved.Body));

            var run = await client.CallToolAsync("payments_payout", AcmePayout);
            Assert.True(run.IsError is null or false, Text(run));
            Assert.Equal("payout ACME 500", string.Join("|", Ledger(app).Actions));

            // Used: the same call again needs a new approval.
            var again = await client.CallToolAsync("payments_payout", AcmePayout);
            Assert.True(Text(again).Contains("Approval required: nothing was done yet") && Link(again) != link, Text(again));
            Assert.Equal(1, Ledger(app).Actions.Count);

            Assert.Equal(
                "ApprovalRequested:ana,ApprovalGranted:bob,Approved:ana,ApprovalRequested:ana",
                string.Join(",", Audit(app).Select(e => $"{e.Decision}:{e.User}")));
        });

        Test("Approvals: a rejection is reported once, and nothing runs", async () =>
        {
            await using var app = await StartAsync();
            await using var client = await Client(app, answer: null);

            var link = Link(await client.CallToolAsync("payments_payout", AcmePayout));
            Assert.Equal("rejected", Status((await Decide(link, approve: false)).Body));

            var told = await client.CallToolAsync("payments_payout", AcmePayout);
            Assert.True(told.IsError == true && Text(told).Contains("Rejected by"), Text(told));
            Assert.True(Text(await client.CallToolAsync("payments_payout", AcmePayout)).Contains("Approval required: nothing was done yet"));
            Assert.Equal(0, Ledger(app).Actions.Count);
            Assert.Equal(409, (await Decide(link, approve: true)).Status);
        });

        Test("Approvals: two different supervisors, neither the caller", async () =>
        {
            await using var app = await StartAsync();
            await using var client = await Client(app, answer: null, user: "ana");
            var call = new Dictionary<string, object?> { ["id"] = 1 };

            var link = Link(await client.CallToolAsync("payments_close", call));

            Assert.Equal(401, (await Decide(link, approve: true)).Status);
            Assert.Equal(403, (await Decide(link, approve: true, user: "eve")).Status);
            Assert.Equal(403, (await Decide(link, approve: true, user: "ana", role: "supervisor")).Status);
            var one = await Decide(link, approve: true, user: "bob", role: "supervisor");
            Assert.Equal("pending", Status(one.Body));
            Assert.Equal(1, one.Body.GetProperty("approvals").GetInt32());
            Assert.Equal(409, (await Decide(link, approve: true, user: "bob", role: "supervisor")).Status);
            Assert.True(Text(await client.CallToolAsync("payments_close", call)).Contains("1 so far"));
            Assert.Equal("approved", Status((await Decide(link, approve: true, user: "carol", role: "supervisor")).Body));

            await client.CallToolAsync("payments_close", call);
            Assert.Equal("close 1", string.Join("|", Ledger(app).Actions));
        });

        Test("Approvals: an approval allows those arguments only", async () =>
        {
            await using var app = await StartAsync();
            await using var client = await Client(app, answer: null, user: "ana");

            var link = Link(await client.CallToolAsync("payments_payout", AcmePayout));
            await Decide(link, approve: true, user: "bob");

            var bigger = await client.CallToolAsync("payments_payout", new Dictionary<string, object?> { ["to"] = "ACME", ["amount"] = 50000 });
            Assert.True(Text(bigger).Contains("Approval required: nothing was done yet"), Text(bigger));
            Assert.Equal(0, Ledger(app).Actions.Count);
        });

        Test("Approvals: when the client cannot confirm, the same user approves through a link", async () =>
        {
            await using var app = await StartAsync(o => o.WhenClientCannotConfirm = ConfirmationFallback.Approval);
            await using var client = await Client(app, answer: null, protocolVersion: "2025-11-25", user: "ana");
            var call = new Dictionary<string, object?> { ["id"] = 1 };

            var first = await client.CallToolAsync("payments_cancel", call);
            var link = Link(first);
            Assert.True(Text(first).Contains("Ask the user to open this link"), Text(first));
            Assert.Equal(403, (await Decide(link, approve: true, user: "bob")).Status);
            Assert.Equal("approved", Status((await Decide(link, approve: true, user: "ana")).Body));

            await client.CallToolAsync("payments_cancel", call);
            Assert.Equal("cancel 1", string.Join("|", Ledger(app).Actions));
        });

        Test("Approvals: the refusal names the protocol the client connected with", async () =>
        {
            await using var app = await StartAsync();
            await using var client = await Client(app, answer: null, protocolVersion: "2025-11-25");

            var result = await client.CallToolAsync("payments_cancel", new Dictionary<string, object?> { ["id"] = 1 });
            Assert.True(Text(result).Contains("2025-11-25"), Text(result));
        });

        Test("Approvals: the decision needs a JSON body; unknown approvals are 404", async () =>
        {
            await using var app = await StartAsync();
            await using var client = await Client(app, answer: null);
            var link = Link(await client.CallToolAsync("payments_payout", AcmePayout));

            using var http = new HttpClient();
            using var form = await http.PostAsync(link, new FormUrlEncodedContent([new("approve", "true")]));
            Assert.Equal(415, (int)form.StatusCode);
            using var missing = await http.GetAsync(app.Urls.First() + "/approvals/" + new string('0', 32));
            Assert.Equal(404, (int)missing.StatusCode);
            Assert.Equal(0, Ledger(app).Actions.Count);
        });

        Test("Approvals: without MapKeelApprovals the host does not start", async () =>
        {
            var error = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                await using var app = await StartAsync(mapApprovals: false);
            });

            Assert.True(error.Message.Contains("MapKeelApprovals"), error.Message);
        });

        Test("Agents: a write the model repeats is answered from the idempotency store, not done twice", async () =>
        {
            await using var app = await StartAsync();
            await using var client = await Client(app, answer: null);
            var charge = new Dictionary<string, object?> { ["id"] = 1, ["amount"] = 10 };

            var first = await client.CallToolAsync("payments_charge", charge);
            var repeated = await client.CallToolAsync("payments_charge", charge);
            var other = await client.CallToolAsync("payments_charge", new Dictionary<string, object?> { ["id"] = 1, ["amount"] = 20 });

            // RequireKey is satisfied: the agent call got a key derived from the call.
            Assert.True(first.IsError is null or false, Text(first));
            Assert.True(repeated.IsError is null or false && Text(repeated).Contains("nothing was done again"), Text(repeated));
            Assert.True(!Text(first).Contains("nothing was done again"), Text(first));
            Assert.Equal("charge 1 10|charge 1 20", string.Join("|", Ledger(app).Actions));
            Assert.True(other.IsError is null or false, Text(other));
        });

        Test("Agents: with IdempotencyKeys off, agent calls get no key", async () =>
        {
            await using var app = await StartAsync(o => o.IdempotencyKeys = false);
            await using var client = await Client(app, answer: null);

            var result = await client.CallToolAsync("payments_charge", new Dictionary<string, object?> { ["id"] = 1, ["amount"] = 10 });

            Assert.True(result.IsError == true && Text(result).StartsWith("400", StringComparison.Ordinal), Text(result));
            Assert.Equal(0, Ledger(app).Actions.Count);
        });

        Test("Approvals: the page says Allow / Deny, in the approver's language", async () =>
        {
            await using var app = await StartAsync(o => o.ApprovalTexts = AgentApprovalTexts.Spanish);
            await using var client = await Client(app, answer: null);
            var link = Link(await client.CallToolAsync("payments_payout", AcmePayout));

            using (var http = new HttpClient())
            {
                var html = await http.GetStringAsync(link);
                Assert.True(html.Contains(">Permitir</button>") && html.Contains(">Denegar</button>") && html.Contains("Permitir que el agente"), html);
            }

            var decided = await Decide(link, approve: true);
            Assert.Equal("Permitido. La próxima llamada del agente lo hará.", decided.Body.GetProperty("result").GetString());

            await using var english = await StartAsync();
            await using var englishClient = await Client(english, answer: null);
            var englishLink = Link(await englishClient.CallToolAsync("payments_payout", AcmePayout));
            using var plain = new HttpClient();
            var englishHtml = await plain.GetStringAsync(englishLink);
            Assert.True(englishHtml.Contains(">Allow</button>") && englishHtml.Contains(">Deny</button>") && englishHtml.Contains("Allow the agent to do this?"), englishHtml);
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

    private static async Task<WebApplication> StartAsync(Action<KeelAgentsOptions>? configure = null, bool mapApprovals = true)
    {
        var builder = WebApplication.CreateBuilder(["--urls", "http://127.0.0.1:0", "--Logging:LogLevel:Default=None"]);
        builder.Services.AddKeelEndpoints();
        builder.Services.AddTestValidation();
        builder.Services.AddProblemDetails();
        builder.Services.AddSingleton<PaymentLedger>();
        builder.Services.AddSingleton<RecordingAuditSink>();
        builder.Services.AddSingleton<IAgentAuditSink>(s => s.GetRequiredService<RecordingAuditSink>());
        builder.Services.AddAuthorization(o => o.AddPolicy("supervisors", p => p.RequireRole("supervisor")));
        builder.Services.AddKeelIdempotency();
        builder.Services.AddKeelMcp();
        builder.Services.AddKeelAgents(configure);
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
        api.MapEndpoint<AgentsFixtures.Payments.Refund.Endpoint>();
        api.MapEndpoint<AgentsFixtures.Payments.Cancel.Endpoint>();
        api.MapEndpoint<AgentsFixtures.Payments.Archive.Endpoint>();
        api.MapEndpoint<AgentsFixtures.Payments.Note.Endpoint>();
        api.MapEndpoint<AgentsFixtures.Payments.List.Endpoint>();
        api.MapEndpoint<AgentsFixtures.Payments.Payout.Endpoint>();
        api.MapEndpoint<AgentsFixtures.Payments.Close.Endpoint>();
        api.MapEndpoint<AgentsFixtures.Payments.Charge.Endpoint>();
        try
        {
            app.MapKeelMcp();
            if (mapApprovals) app.MapKeelApprovals();
            await app.StartAsync();
        }
        catch
        {
            await app.DisposeAsync();
            throw;
        }

        return app;
    }

    private static Task<McpClient> Client(WebApplication app, Func<ElicitRequestParams, ValueTask<ElicitResult>>? answer, string? protocolVersion = null, string? user = null)
    {
        var options = new McpClientOptions { ProtocolVersion = protocolVersion };
        if (answer is not null) options.Handlers.ElicitationHandler = (request, _) => answer(request!);
        var headers = new Dictionary<string, string>();
        if (user is not null) headers["X-User"] = user;
        return McpClient.CreateAsync(
            new HttpClientTransport(new HttpClientTransportOptions { Endpoint = new Uri(app.Urls.First() + "/mcp"), AdditionalHeaders = headers }),
            options);
    }

    private static string Link(CallToolResult result) =>
        Regex.Match(Text(result), @"https?://\S+/approvals/[0-9a-f]{32}") is { Success: true } match
            ? match.Value
            : throw new AssertionException("no approval link in: " + Text(result));

    private static async Task<(int Status, JsonElement Body)> Decide(string link, bool approve, string? user = null, string? role = null)
    {
        using var http = new HttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, link)
        {
            Content = new StringContent(approve ? """{"approve":true}""" : """{"approve":false}""", Encoding.UTF8, "application/json"),
        };
        if (user is not null) request.Headers.Add("X-User", user);
        if (role is not null) request.Headers.Add("X-Role", role);
        using var response = await http.SendAsync(request);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return ((int)response.StatusCode, json.RootElement.Clone());
    }

    private static string Status(JsonElement body) => body.TryGetProperty("status", out var status) ? status.GetString() ?? "" : "";

    private static readonly Dictionary<string, object?> AcmePayout = new() { ["to"] = "ACME", ["amount"] = 500 };
}
