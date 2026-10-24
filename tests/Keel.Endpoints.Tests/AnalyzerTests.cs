using System.Collections.Immutable;
using Keel.Endpoints.Analyzers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using static Keel.Endpoints.Tests.Harness;

namespace Keel.Endpoints.Tests;

internal static class AnalyzerTests
{
    private const string Usings = """
        using System.ComponentModel.DataAnnotations;
        using System.Threading;
        using System.Threading.Tasks;
        using Keel.Endpoints;
        using Microsoft.AspNetCore.Mvc;
        using Microsoft.Extensions.Validation;

        """;

    public static void Register()
    {
        Test("Analyzer: clean endpoints produce no diagnostics", async () =>
        {
            var ids = await DiagnosticsAsync("""
                public sealed record GetThing(string Id);
                [Get("things/{id}")]
                public sealed class GetThingEndpoint : Endpoint<GetThing, string>
                {
                    public override Task<string> HandleAsync(GetThing r, CancellationToken ct) => Task.FromResult(r.Id);
                }
                """);
            Assert.Equal("", string.Join(",", ids));
        });

        Test("KEEL001: two endpoints on the same method and template (parameter names ignored)", async () =>
        {
            var ids = await DiagnosticsAsync("""
                public sealed record A(string Id);
                public sealed record B(string Key);
                [Get("things/{id}")]
                public sealed class First : Endpoint<A, string> { public override Task<string> HandleAsync(A r, CancellationToken ct) => Task.FromResult(""); }
                [Get("/Things/{key:int}/")]
                public sealed class Second : Endpoint<B, string> { public override Task<string> HandleAsync(B r, CancellationToken ct) => Task.FromResult(""); }
                [Post("things/{id}")]
                public sealed class Third : EndpointWithoutRequest<string> { public override Task<string> HandleAsync(CancellationToken ct) => Task.FromResult(""); }
                """);
            Assert.Equal("KEEL001", string.Join(",", ids));
        });

        Test("KEEL001: the same route in two API versions (folders or [ApiVersion]) is not a duplicate", async () =>
        {
            var ids = await DiagnosticsAsync("""
                namespace Asp.Versioning
                {
                    [System.AttributeUsage(System.AttributeTargets.Class, AllowMultiple = true)]
                    public sealed class ApiVersionAttribute(double version) : System.Attribute;
                }
                namespace Shop.V1.Orders { public sealed record GetOrder(string Id); [Get("orders/{id}")] public sealed class Get : Endpoint<GetOrder, string> { public override Task<string> HandleAsync(GetOrder r, CancellationToken ct) => Task.FromResult(""); } }
                namespace Shop.V2.Orders { public sealed record GetOrder(string Id); [Get("orders/{id}")] public sealed class Get : Endpoint<GetOrder, string> { public override Task<string> HandleAsync(GetOrder r, CancellationToken ct) => Task.FromResult(""); } }
                namespace Shop.Items
                {
                    public sealed record GetItem(string Id);
                    [Get("items/{id}"), Asp.Versioning.ApiVersion(1.0)] public sealed class GetV1 : Endpoint<GetItem, string> { public override Task<string> HandleAsync(GetItem r, CancellationToken ct) => Task.FromResult(""); }
                    [Get("items/{id}"), Asp.Versioning.ApiVersion(2.0)] public sealed class GetV2 : Endpoint<GetItem, string> { public override Task<string> HandleAsync(GetItem r, CancellationToken ct) => Task.FromResult(""); }
                    [Get("/Items/{id}/"), Asp.Versioning.ApiVersion(2.0)] public sealed class GetV2Again : Endpoint<GetItem, string> { public override Task<string> HandleAsync(GetItem r, CancellationToken ct) => Task.FromResult(""); }
                }
                """);
            // Only GetV2Again collides: same route, same version as GetV2.
            Assert.Equal("KEEL001", string.Join(",", ids));
        });

        Test("KEEL002: a route parameter with no contract member", async () =>
        {
            var ids = await DiagnosticsAsync("""
                public sealed record GetThing(string Id);
                [Get("tenants/{tenantId}/things/{id}")]
                public sealed class GetThingEndpoint : Endpoint<GetThing, string>
                {
                    public override Task<string> HandleAsync(GetThing r, CancellationToken ct) => Task.FromResult(r.Id);
                }
                """);
            Assert.Equal("KEEL002", string.Join(",", ids));
        });

        Test("KEEL003: a route parameter on a body-bound contract; [BindRequest(Parameters)] fixes it", async () =>
        {
            var ids = await DiagnosticsAsync("""
                public sealed record Rename(string Id, string Name);
                [Put("things/{id}")]
                public sealed class RenameEndpoint : Endpoint<Rename> { public override Task HandleAsync(Rename r, CancellationToken ct) => Task.CompletedTask; }

                public sealed record Body(string Name);
                [BindRequest(RequestBinding.Parameters)]
                public sealed record Rename2(string Id, [property: FromBody] Body Body);
                [Put("others/{id}")]
                public sealed class Rename2Endpoint : Endpoint<Rename2> { public override Task HandleAsync(Rename2 r, CancellationToken ct) => Task.CompletedTask; }
                """);
            Assert.Equal("KEEL003", string.Join(",", ids));
        });

        Test("KEEL004: validation attributes without [ValidatableType]", async () =>
        {
            var ids = await DiagnosticsAsync("""
                public sealed record Create([property: Required] string Name);
                [Post("things")]
                public sealed class CreateEndpoint : Endpoint<Create> { public override Task HandleAsync(Create r, CancellationToken ct) => Task.CompletedTask; }

                [ValidatableType]
                public sealed record Create2([property: Required] string Name);
                [Post("others")]
                public sealed class Create2Endpoint : Endpoint<Create2> { public override Task HandleAsync(Create2 r, CancellationToken ct) => Task.CompletedTask; }
                """);
            Assert.Equal("KEEL004", string.Join(",", ids));
        });

        Test("KEEL004: a contract wrapping a [ValidatableType] member must be marked too", async () =>
        {
            var ids = await DiagnosticsAsync("""
                [ValidatableType]
                public sealed record Body([property: Required] string Name);
                [BindRequest(RequestBinding.Parameters)]
                public sealed record Rename(string Id, [property: FromBody] Body Body);
                [Put("things/{id}")]
                public sealed class RenameEndpoint : Endpoint<Rename> { public override Task HandleAsync(Rename r, CancellationToken ct) => Task.CompletedTask; }
                """);
            Assert.Equal("KEEL004", string.Join(",", ids));
        });
        Test("KEEL007: collection items with rules need [ValidatableType]", async () =>
        {
            var ids = await DiagnosticsAsync("""
                public sealed record Line([property: Range(1, 10)] int Quantity);
                [ValidatableType]
                public sealed record Order([property: Required] Line[] Lines, System.Collections.Generic.List<Line>? More);
                [Post("orders")]
                public sealed class OrderEndpoint : Endpoint<Order> { public override Task HandleAsync(Order r, CancellationToken ct) => Task.CompletedTask; }
                """);
            Assert.Equal("KEEL007", string.Join(",", ids));   // once per item type
        });

        Test("KEEL007: not for marked items, items reached directly, or items without rules", async () =>
        {
            var ids = await DiagnosticsAsync("""
                [ValidatableType]
                public sealed record Marked([property: Range(1, 10)] int Quantity);
                public sealed record Direct([property: Range(1, 10)] int Quantity);
                public sealed record Plain(int Quantity);
                [ValidatableType]
                public sealed record Order(Marked[] A, Direct[] B, Direct First, System.Collections.Generic.IReadOnlyList<Plain> C, System.Collections.Generic.Dictionary<string, Plain> D, string[] Tags);
                [Post("orders")]
                public sealed class OrderEndpoint : Endpoint<Order> { public override Task HandleAsync(Order r, CancellationToken ct) => Task.CompletedTask; }
                """);
            Assert.Equal("", string.Join(",", ids));
        });

        Test("KEEL007: items nested below a member are found too", async () =>
        {
            var ids = await DiagnosticsAsync("""
                public sealed record Line([property: Required] string Sku);
                [ValidatableType]
                public sealed record Cart(Line[] Lines);
                [BindRequest(RequestBinding.Parameters), ValidatableType]
                public sealed record Update(string Id, [property: FromBody] Cart Body);
                [Put("carts/{id}")]
                public sealed class UpdateEndpoint : Endpoint<Update> { public override Task HandleAsync(Update r, CancellationToken ct) => Task.CompletedTask; }
                """);
            Assert.Equal("KEEL007", string.Join(",", ids));
        });
    }

    private static async Task<IReadOnlyList<string>> DiagnosticsAsync(string source)
    {
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Where(p => p.EndsWith(".dll", StringComparison.Ordinal))
            .Select(p => MetadataReference.CreateFromFile(p))
            .Append(MetadataReference.CreateFromFile(typeof(EndpointBase).Assembly.Location));

        var compilation = CSharpCompilation.Create(
            "AnalyzerTest",
            [CSharpSyntaxTree.ParseText(Usings + source)],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                .WithSpecificDiagnosticOptions([new("ASP0029", ReportDiagnostic.Suppress)]));

        var compileErrors = compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        if (compileErrors.Count > 0)
            throw new AssertionException("test source does not compile: " + string.Join("; ", compileErrors));

        var diagnostics = await compilation
            .WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new EndpointAnalyzer()))
            .GetAnalyzerDiagnosticsAsync();
        return [.. diagnostics.Select(d => d.Id).OrderBy(id => id, StringComparer.Ordinal)];
    }
}
