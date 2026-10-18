using Keel.Endpoints.Analyzers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using static Keel.Endpoints.Tests.Harness;

namespace Keel.Endpoints.Tests;

/// <summary>Runs the registration generator over small sources and checks what it writes.</summary>
internal static class GeneratorTests
{
    private const string Usings = """
        using System.Collections.Generic;
        using System.Threading;
        using System.Threading.Tasks;
        using Keel.Endpoints;
        using Microsoft.AspNetCore.Http.HttpResults;

        """;

    public static void Register()
    {
        Test("Generator: one typed MapMethods call per endpoint, binding by verb", () =>
        {
            var (source, diagnostics, errors) = Run("""
                namespace Shop.Orders.Get { public sealed record GetOrder(string Id);
                  [Get("orders/{id}")] public sealed class Endpoint : Endpoint<GetOrder, string>
                  { public override Task<string> HandleAsync(GetOrder r, CancellationToken ct) => Task.FromResult(r.Id); } }
                namespace Shop.Orders.Create { public sealed record CreateOrder(string Name);
                  [Post("orders")] public sealed class Endpoint : Endpoint<CreateOrder>
                  { public override Task HandleAsync(CreateOrder r, CancellationToken ct) => Task.CompletedTask; } }
                """);

            Assert.Equal("", string.Join(",", diagnostics) + string.Join(",", errors));
            Assert.True(source.Contains("""group.MapMethods("orders/{id}", new[] { "GET" },"""));
            Assert.True(source.Contains("[global::Microsoft.AspNetCore.Http.AsParameters] global::Shop.Orders.Get.GetOrder request"));
            Assert.True(source.Contains("[global::Microsoft.AspNetCore.Mvc.FromBody] global::Shop.Orders.Create.CreateOrder request"));
            Assert.True(source.Contains("TypedResults.NoContent()"));     // Endpoint<TRequest> answers 204
            Assert.True(!source.Contains("MakeGenericMethod") && !source.Contains("GetTypes"));
        });

        Test("Generator: [BindRequest], [ServerSentEvents] and EndpointWithoutRequest", () =>
        {
            var (source, _, errors) = Run("""
                [BindRequest(RequestBinding.Parameters)] public sealed record Rename(string Id);
                [Put("things/{id}")] public sealed class RenameEndpoint : Endpoint<Rename>
                { public override Task HandleAsync(Rename r, CancellationToken ct) => Task.CompletedTask; }

                public sealed record Watch(int Count);
                [Get("watch"), ServerSentEvents("tick")] public sealed class WatchEndpoint : StreamEndpoint<Watch, int>
                { public override async IAsyncEnumerable<int> HandleAsync(Watch r, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct) { await Task.Yield(); yield return 1; } }

                [Get("ping")] public sealed class PingEndpoint : EndpointWithoutRequest<string>
                { public override Task<string> HandleAsync(CancellationToken ct) => Task.FromResult("pong"); }
                """);

            Assert.Equal("", string.Join(",", errors));
            Assert.True(source.Contains("[global::Microsoft.AspNetCore.Http.AsParameters] global::Rename request")); // PUT, but [BindRequest]
            Assert.True(source.Contains("TypedResults.ServerSentEvents("));
            Assert.True(source.Contains("static (global::Microsoft.AspNetCore.Http.HttpContext context, global::System.Threading.CancellationToken cancellationToken) => global::Keel.Endpoints.Infrastructure.GeneratedEndpoints.Activate<global::PingEndpoint>(context).HandleAsync(cancellationToken)"));
        });

        Test("Generator: KEEL005 for a private nested endpoint, which is left out", () =>
        {
            var (source, diagnostics, errors) = Run("""
                public static class Outer
                {
                    private sealed class Hidden : EndpointWithoutRequest<string>
                    { public override Task<string> HandleAsync(CancellationToken ct) => Task.FromResult(""); }
                }
                """, extra: "[Get(\"hidden\")]", insertBefore: "private sealed class Hidden");

            Assert.Equal("", string.Join(",", errors));
            Assert.Equal("KEEL005", string.Join(",", diagnostics));
            Assert.True(!source.Contains("Hidden"));
        });

        Test("Generator: emits nothing when the registration is already ordinary source (Native AOT stage 2)", () =>
        {
            var (_, _, errors, trees) = RunWithTrees("""
                namespace Microsoft.AspNetCore.Builder { internal static class KeelGeneratedEndpointRouteBuilderExtensions { } }
                [Get("ping")] public sealed class PingEndpoint : EndpointWithoutRequest<string>
                { public override Task<string> HandleAsync(CancellationToken ct) => Task.FromResult("pong"); }
                """);

            Assert.Equal("", string.Join(",", errors));
            Assert.Equal(0, trees);
        });

        Test("Generator: endpoint groups are created in code, outermost namespace first", () =>
        {
            var (source, diagnostics, errors) = Run("""
                namespace Shop { public sealed class ShopGroup : EndpointGroup { public override void Configure(EndpointOptions o) => o.Tags("Shop"); } }
                namespace Shop.Admin { public sealed class AdminGroup : EndpointGroup { public override void Configure(EndpointOptions o) => o.RequireAuthorization("admin"); }
                  [Get("admin/ping")] public sealed class PingEndpoint : EndpointWithoutRequest<string>
                  { public override Task<string> HandleAsync(CancellationToken ct) => Task.FromResult(""); } }
                namespace Other { [Get("other")] public sealed class OtherEndpoint : EndpointWithoutRequest<string>
                  { public override Task<string> HandleAsync(CancellationToken ct) => Task.FromResult(""); } }
                """);

            Assert.Equal("", string.Join(",", diagnostics) + string.Join(",", errors));
            Assert.True(source.Contains("new global::Keel.Endpoints.EndpointGroup[] { new global::Shop.ShopGroup(), new global::Shop.Admin.AdminGroup() }"));
            Assert.True(source.Contains("global::System.Array.Empty<global::Keel.Endpoints.EndpointGroup>()")); // Other: no group
        });

        Test("Generator: KEEL006 for a group without a public parameterless constructor", () =>
        {
            var (source, diagnostics, errors) = Run("""
                public sealed class NeedsArgs : EndpointGroup
                { public NeedsArgs(string name) { } public override void Configure(EndpointOptions o) { } }
                [Get("ping")] public sealed class PingEndpoint : EndpointWithoutRequest<string>
                { public override Task<string> HandleAsync(CancellationToken ct) => Task.FromResult(""); }
                """);

            Assert.Equal("", string.Join(",", errors));
            Assert.Equal("KEEL006", string.Join(",", diagnostics));
            Assert.True(!source.Contains("NeedsArgs"));
        });

        Test("Generator: the generated code compiles and maps the app with MapKeelEndpoints(prefix)", () =>
        {
            var (_, _, errors) = Run("""
                [Get("ping")] public sealed class PingEndpoint : EndpointWithoutRequest<string>
                { public override Task<string> HandleAsync(CancellationToken ct) => Task.FromResult("pong"); }
                public static class Startup
                {
                    public static void Map(Microsoft.AspNetCore.Routing.IEndpointRouteBuilder app) =>
                        Microsoft.AspNetCore.Builder.KeelGeneratedEndpointRouteBuilderExtensions.MapKeelEndpoints(app, "/api");
                }
                """);
            Assert.Equal("", string.Join(",", errors));
        });
    }

    private static (string Source, List<string> Diagnostics, List<string> Errors) Run(string code, string? extra = null, string? insertBefore = null)
    {
        var (source, diagnostics, errors, _) = RunWithTrees(code, extra, insertBefore);
        return (source, diagnostics, errors);
    }

    private static (string Source, List<string> Diagnostics, List<string> Errors, int GeneratedTrees) RunWithTrees(
        string code, string? extra = null, string? insertBefore = null)
    {
        if (extra is not null && insertBefore is not null) code = code.Replace(insertBefore, extra + " " + insertBefore);

        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Where(p => p.EndsWith(".dll", StringComparison.Ordinal))
            .Select(p => MetadataReference.CreateFromFile(p))
            .Append(MetadataReference.CreateFromFile(typeof(EndpointBase).Assembly.Location));

        var compilation = CSharpCompilation.Create("GeneratorTest",
            [CSharpSyntaxTree.ParseText(Usings + code)], references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        var driver = CSharpGeneratorDriver.Create(new RegistrationGenerator()).RunGeneratorsAndUpdateCompilation(compilation, out var output, out var generatorDiagnostics);
        var result = driver.GetRunResult();

        var source = string.Concat(result.GeneratedTrees.Select(t => t.ToString()));
        var errors = output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.ToString()).ToList();
        return (source, [.. generatorDiagnostics.Select(d => d.Id)], errors, result.GeneratedTrees.Length);
    }
}
