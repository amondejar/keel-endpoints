using System.ComponentModel.DataAnnotations;
using Keel.Endpoints.Testing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using static Keel.Endpoints.Tests.Harness;

namespace Keel.Endpoints.Tests;

internal static class ManifestTests
{
    public static void Register()
    {
        Test("Manifest: the sample API matches its committed api.manifest.json", async () =>
        {
            await using var host = await SampleHost.StartAsync();
            ApiManifest.Verify(host.Services, "../../samples/Keel.Sample/api.manifest.json");
        });

        Test("Manifest: describes routes, parameters, body, responses, authorization and JSON shapes", async () =>
        {
            await using var app = await ThingsAppAsync();
            var manifest = ApiManifest.Create(app.Services);
            var e = manifest.Endpoints["POST things/{id}"];

            Assert.Equal("required", e.Authorization);
            Assert.Equal("admin", string.Join(",", e.Roles));
            Assert.Equal("ApiKey", string.Join(",", e.Schemes));
            Assert.Equal("id=string", string.Join(",", e.Path.Select(p => $"{p.Key}={p.Value}")));
            Assert.Equal("int32?", e.Query["page"]);
            Assert.Equal("string (required)", e.Header["X-Tenant"]);
            Assert.Equal("Thing (required)", e.Body);
            Assert.Equal("Tree", e.Responses["200"]);
            Assert.Equal("", e.Responses["404"]);

            var thing = manifest.Types["Thing"].Properties!;
            Assert.Equal("string (required)", thing["name"]);   // [Required]
            Assert.Equal("int32 (required)", thing["size"]);    // required member
            Assert.Equal("string?", thing["note"]);
            Assert.Equal("Color", thing["color"]);
            Assert.Equal("map<int32>", thing["counts"]);
            Assert.Equal("Red,Green", string.Join(",", manifest.Types["Color"].Values!));
            Assert.Equal("array<Tree>", manifest.Types["Tree"].Properties!["children"]);   // recursive type terminates

            Assert.Equal("anonymous", manifest.Endpoints["GET ping"].Authorization);
            Assert.Equal("string as text/plain", manifest.Endpoints["GET ping"].Responses["200"]);
        });

        Test("Manifest: JSON round-trips and is stable", async () =>
        {
            await using var app = await ThingsAppAsync();
            var json = ApiManifest.Create(app.Services).ToJson();
            Assert.Equal(json, ApiManifest.Parse(json).ToJson());
            Assert.True(json.Contains("\"array<Tree>\"") && !json.Contains('\r'));
            Assert.Equal(0, ApiManifest.Compare(ApiManifest.Parse(json), ApiManifest.Parse(json)).Count);
        });

        Test("Manifest diff: removed endpoint and response member are breaking; additions are compatible", () =>
        {
            var changes = Diff(
                """{ "GET a": { "responses": { "200": "A" } }, "GET gone": { "responses": { "200": "" } } }""",
                """{ "A": { "id": "string", "total": "number" } }""",
                """{ "GET a": { "responses": { "200": "A" } }, "GET b": { "responses": { "200": "" } } }""",
                """{ "A": { "id": "string", "note": "string?" } }""");
            Assert.Equal(
                "Breaking GET a: response 200.total removed|Breaking GET gone: removed|Compatible GET a: response 200.note added|Compatible GET b: added",
                changes);
        });

        Test("Manifest diff: MCP tools: gone or renamed is breaking, less confirmation or a new write is security", () =>
        {
            var changes = Diff(
                """
                {
                  "GET a": { "responses": { "200": "" }, "tool": { "name": "a_get", "description": "A.", "hints": ["read-only"] } },
                  "POST b": { "responses": { "200": "" }, "tool": { "name": "b_create", "description": "B.", "hints": ["destructive", "idempotent"] } },
                  "GET c": { "responses": { "200": "" }, "tool": { "name": "c", "description": "Old.", "hints": ["read-only"] } }
                }
                """,
                "{}",
                """
                {
                  "GET a": { "responses": { "200": "" } },
                  "POST b": { "responses": { "200": "" }, "tool": { "name": "b_new", "description": "B.", "hints": ["non-destructive"] } },
                  "GET c": { "responses": { "200": "" }, "tool": { "name": "c", "description": "New.", "hints": ["read-only"] } },
                  "POST d": { "responses": { "200": "" }, "tool": { "name": "d_create", "description": "D.", "hints": ["non-destructive"] } }
                }
                """,
                "{}");
            Assert.Equal(
                "Breaking GET a: no longer an MCP tool (was 'a_get'): agents that use it stop working|" +
                "Breaking POST b: MCP tool 'b_new' is no longer idempotent: agents must not retry it|" +
                "Breaking POST b: MCP tool renamed from 'b_create' to 'b_new': agents that use it stop working|" +
                "Security POST b: MCP tool 'b_new' is no longer marked destructive: agents may call it without asking the user|" +
                "Security POST d: now the MCP tool 'd_create', which changes data: agents can call it|" +
                "Compatible GET c: MCP tool 'c' description changed to \"New.\"|" +
                "Compatible POST d: added",
                changes);
        });

        Test("Manifest diff: new required request members and parameters are breaking, optional ones are not", () =>
        {
            var changes = Diff(
                """{ "POST a": { "query": { "page": "int32" }, "body": "In (required)" } }""",
                """{ "In": { "name": "string", "old": "string" } }""",
                """{ "POST a": { "query": { "page": "int32 (required)", "tenant": "string (required)", "sort": "string" }, "body": "In (required)" } }""",
                """{ "In": { "name": "string (required)", "code": "string (required)", "tag": "string" } }""");
            Assert.Equal(
                "Breaking POST a: query parameter 'page' is now required|Breaking POST a: request body.code added as required|" +
                "Breaking POST a: request body.name is now required|Breaking POST a: requires new query parameter 'tenant'|" +
                "Compatible POST a: accepts new optional query parameter 'sort'|Compatible POST a: request body.old removed (now ignored)|" +
                "Compatible POST a: request body.tag added (optional)",
                changes);
        });

        Test("Manifest diff: authorization that gets stricter is breaking, looser is a security change", () =>
        {
            var changes = Diff(
                """{ "GET open": { "authorization": "anonymous" }, "GET admin": { "authorization": "required", "policies": ["admin"] }, "GET user": { "authorization": "required" } }""",
                "{}",
                """{ "GET open": { "authorization": "required" }, "GET admin": { "authorization": "required" }, "GET user": { "authorization": "anonymous" } }""",
                "{}");
            Assert.Equal(
                "Breaking GET open: now requires authorization|Security GET admin: no longer requires policy 'admin'|" +
                "Security GET user: no longer requires authorization (now anonymous)",
                changes);
        });

        Test("Manifest diff: route moves, parameter renames and operation renames", () =>
        {
            var changes = Diff(
                """{ "GET things/{id}": { "operation": "GetThing", "path": { "id": "string" } }, "GET old/path": { "operation": "List" } }""",
                "{}",
                """{ "GET things/{thingId}": { "operation": "ThingsGet", "path": { "thingId": "string" } }, "GET new/path": { "operation": "List" } }""",
                "{}");
            Assert.Equal(
                "Breaking GET old/path: moved to GET new/path|" +
                "Compatible GET things/{id}: operation name changed from GetThing to ThingsGet (generated clients may rename their method)|" +
                "Compatible GET things/{id}: route template changed to things/{thingId} (same URL shape)",
                changes);
        });

        Test("Manifest diff: types, numbers, nullability, media types, enums and status codes", () =>
        {
            var changes = Diff(
                """
                {
                  "POST a": { "path": { "id": "int32" }, "body": "Color", "responses": { "200": "Out", "404": "" } },
                  "GET s": { "responses": { "200": "Out", "201": "" } }
                }
                """,
                """{ "Out": { "count": "int32", "name": "string", "when": "date-time", "color": "Color" }, "Color": ["Red", "Green"] }""",
                """
                {
                  "POST a": { "path": { "id": "int64" }, "body": "Color", "responses": { "200": "Out" } },
                  "GET s": { "responses": { "200": "Out as text/event-stream" } }
                }
                """,
                """{ "Out": { "count": "int64", "name": "string?", "when": "string", "color": "Color" }, "Color": ["Red", "Blue"] }""");
            Assert.Equal(
                "Breaking GET s: no longer answers 201|Breaking GET s: response 200 is no longer application/json|" +
                "Breaking GET s: response 200.count changed from int32 to int64|Breaking GET s: response 200.name may now be null|" +
                "Breaking GET s: response 200.when changed from date-time to string|" +
                "Breaking POST a: request body no longer accepts Green|" +
                "Breaking POST a: response 200.count changed from int32 to int64|Breaking POST a: response 200.name may now be null|" +
                "Breaking POST a: response 200.when changed from date-time to string|" +
                "Compatible GET s: response 200 may now be text/event-stream|" +
                "Compatible GET s: response 200.color may now be Blue|Compatible GET s: response 200.color no longer returns Green|" +
                "Compatible POST a: no longer documents 404|Compatible POST a: request body also accepts Blue|" +
                "Compatible POST a: response 200.color may now be Blue|Compatible POST a: response 200.color no longer returns Green",
                changes);
        });

        Test("Manifest verify: creates a missing file, then fails on changes until they are accepted", async () =>
        {
            var folder = Directory.CreateTempSubdirectory("keel-manifest-").FullName;
            var file = Path.Combine(folder, "api.manifest.json");
            try
            {
                await using var app = await ThingsAppAsync();
                var none = new ApiManifestOptions { Update = ApiManifestUpdate.None };

                var created = Throws(() => ApiManifest.Verify(app.Services, file, none));
                Assert.True(File.Exists(file) && created.Message.Contains("created") && created.Changes.Count == 0);
                ApiManifest.Verify(app.Services, file, none);   // matches now

                // The baseline had an extra endpoint and lacked one: one breaking, one compatible change.
                var json = File.ReadAllText(file).Replace("\"GET ping\"", "\"GET pong\"");
                File.WriteAllText(file, json);
                var failed = Throws(() => ApiManifest.Verify(app.Services, file, none));
                Assert.True(failed.Message.Contains("1 breaking") && failed.Message.Contains("GET pong: removed"));

                Throws(() => ApiManifest.Verify(app.Services, file, new ApiManifestOptions { Update = ApiManifestUpdate.Compatible }));
                Assert.Equal(json, File.ReadAllText(file));      // compatible-only update refuses breaking changes

                ApiManifest.Verify(app.Services, file, new ApiManifestOptions { Update = ApiManifestUpdate.All });
                Assert.True(File.ReadAllText(file).Contains("\"GET ping\""));

                // Only an addition: accepted by a compatible update.
                var withoutPing = ApiManifest.Parse(File.ReadAllText(file));
                File.WriteAllText(file, new ApiManifest
                {
                    Endpoints = withoutPing.Endpoints.Where(e => e.Key != "GET ping").ToDictionary(),
                    Types = withoutPing.Types,
                }.ToJson());
                ApiManifest.Verify(app.Services, file, new ApiManifestOptions { Update = ApiManifestUpdate.Compatible });
                ApiManifest.Verify(app.Services, file, none);
            }
            finally
            {
                Directory.Delete(folder, recursive: true);
            }
        });
    }

    private static ApiManifestException Throws(Action body)
    {
        try { body(); }
        catch (ApiManifestException e) { return e; }
        throw new AssertionException("expected ApiManifestException, nothing was thrown");
    }

    /// <summary>Compares two hand-written manifests; returns "Kind endpoint: description" joined by '|'.</summary>
    private static string Diff(string oldEndpoints, string oldTypes, string newEndpoints, string newTypes)
    {
        static ApiManifest Manifest(string endpoints, string types) =>
            ApiManifest.Parse($$"""{ "format": 1, "endpoints": {{endpoints}}, "types": {{types}} }""");

        return string.Join("|", ApiManifest.Compare(Manifest(oldEndpoints, oldTypes), Manifest(newEndpoints, newTypes))
            .Select(c => $"{c.Kind} {c.Endpoint}: {c.Description}"));
    }

    private static async Task<WebApplication> ThingsAppAsync()
    {
        var builder = WebApplication.CreateBuilder(["--urls", "http://127.0.0.1:0", "--Logging:LogLevel:Default=None"]);
        builder.Services.AddKeelEndpoints();
        builder.Services.AddAuthorization();
        var app = builder.Build();

        app.MapPost("/things/{id}", Results<Ok<Tree>, NotFound> (string id, [FromQuery] int? page, [FromHeader(Name = "X-Tenant")] string tenant, Thing thing) =>
                TypedResults.Ok(new Tree(id, [])))
            .RequireAuthorization(new AuthorizeAttribute { Roles = "admin", AuthenticationSchemes = "ApiKey" });
        app.MapGet("/ping", () => "pong").AllowAnonymous();

        await app.StartAsync();
        return app;
    }

    public sealed record Thing([property: Required] string Name, string? Note, Color Color, Dictionary<string, int> Counts)
    {
        public required int Size { get; init; }
    }

    public enum Color { Red, Green }

    public sealed record Tree(string Label, List<Tree> Children);
}
