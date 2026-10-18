using System.Collections.Immutable;
using Keel.Endpoints.Analyzers;
using Keel.Endpoints.CodeFixes;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using static Keel.Endpoints.Tests.Harness;

namespace Keel.Endpoints.Tests;

/// <summary>
/// Runs each code fix in an in-memory workspace and checks that the fixed code compiles and no longer
/// reports the diagnostic. Set KEEL_PRINT_FIXES=1 to print the fixed sources.
/// </summary>
internal static class CodeFixTests
{
    private const string Usings = """
        using System.ComponentModel.DataAnnotations;
        using System.Threading;
        using System.Threading.Tasks;
        using Keel.Endpoints;

        """;

    public static void Register()
    {
        Test("Code fix KEEL002: adds a constructor parameter to a positional record", async () =>
        {
            var (fixedSources, remaining) = await FixAsync(new AddContractMemberFix(), "KEEL002", ("Endpoint.cs", Usings + """
                public sealed record GetThing(string Id);
                [Get("tenants/{tenantId}/things/{id}")]
                public sealed class GetThingEndpoint : Endpoint<GetThing, string>
                {
                    public override Task<string> HandleAsync(GetThing r, CancellationToken ct) => Task.FromResult(r.Id);
                }
                """));
            Assert.True(fixedSources[0].Contains("record GetThing(string Id, string TenantId)"));
            Assert.Equal("", remaining);
        });

        Test("Code fix KEEL002: adds an init property to a class contract in another file", async () =>
        {
            var (fixedSources, remaining) = await FixAsync(new AddContractMemberFix(), "KEEL002",
                ("Endpoint.cs", Usings + """
                    [Get("tenants/{tenantId}/things")]
                    public sealed class ListThings : Endpoint<ListThingsRequest, string>
                    {
                        public override Task<string> HandleAsync(ListThingsRequest r, CancellationToken ct) => Task.FromResult("");
                    }
                    """),
                ("Contracts.cs", """
                    public sealed class ListThingsRequest
                    {
                        public int Page { get; init; }
                    }
                    """));
            Assert.True(fixedSources[1].Contains("public string TenantId { get; init; } = string.Empty;"));
            Assert.Equal("", remaining);
        });

        Test("Code fix KEEL003: binds the contract from parameters", async () =>
        {
            var (fixedSources, remaining) = await FixAsync(new BindAsParametersFix(), "KEEL003", ("Endpoint.cs", Usings + """
                /// <summary>Renames a thing.</summary>
                public sealed record Rename(string Id, string Name);
                [Put("things/{id}")]
                public sealed class RenameEndpoint : Endpoint<Rename> { public override Task HandleAsync(Rename r, CancellationToken ct) => Task.CompletedTask; }
                """));
            Assert.True(fixedSources[0].Contains("[BindRequest(RequestBinding.Parameters)]"));
            Assert.True(fixedSources[0].IndexOf("<summary>", StringComparison.Ordinal) < fixedSources[0].IndexOf("[BindRequest", StringComparison.Ordinal));
            Assert.Equal("", remaining);
        });

        Test("Code fix KEEL004: adds [ValidatableType] and its using", async () =>
        {
            var (fixedSources, remaining) = await FixAsync(new AddValidatableTypeFix(), "KEEL004", ("Endpoint.cs", Usings + """
                public sealed record Create([property: Required] string Name);
                [Post("things")]
                public sealed class CreateEndpoint : Endpoint<Create> { public override Task HandleAsync(Create r, CancellationToken ct) => Task.CompletedTask; }
                """));
            Assert.True(fixedSources[0].Contains("using Microsoft.Extensions.Validation;"));
            Assert.True(fixedSources[0].Contains("[ValidatableType]"));
            Assert.Equal("", remaining);
        });
    }

    /// <summary>Applies the first fix offered for <paramref name="id"/>; returns the fixed sources and the
    /// Keel diagnostics left afterwards.</summary>
    private static async Task<(string[] Sources, string Remaining)> FixAsync(
        CodeFixProvider provider, string id, params (string Name, string Text)[] sources)
    {
        using var workspace = new AdhocWorkspace();
        var project = workspace.AddProject(ProjectInfo.Create(
            ProjectId.CreateNewId(), VersionStamp.Create(), "CodeFixTest", "CodeFixTest", LanguageNames.CSharp,
            compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                .WithSpecificDiagnosticOptions([new("ASP0029", ReportDiagnostic.Suppress)]),
            metadataReferences: References()));

        var documentIds = new List<DocumentId>();
        foreach (var (name, text) in sources)
        {
            var document = workspace.AddDocument(project.Id, name, SourceText.From(text));
            documentIds.Add(document.Id);
        }

        var solution = workspace.CurrentSolution;
        var diagnostic = (await DiagnosticsAsync(solution.GetProject(project.Id)!)).FirstOrDefault(d => d.Id == id)
            ?? throw new AssertionException($"{id} was not reported on the test source");

        var target = solution.GetDocument(diagnostic.Location.SourceTree)!;
        var actions = new List<CodeAction>();
        await provider.RegisterCodeFixesAsync(new CodeFixContext(target, diagnostic, (a, _) => actions.Add(a), CancellationToken.None));
        if (actions.Count == 0) throw new AssertionException($"no code fix offered for {id}");

        var operations = await actions[0].GetOperationsAsync(CancellationToken.None);
        var changed = operations.OfType<ApplyChangesOperation>().Single().ChangedSolution;

        var fixedSources = new string[documentIds.Count];
        for (var i = 0; i < documentIds.Count; i++)
        {
            var document = await Microsoft.CodeAnalysis.Formatting.Formatter.FormatAsync(
                changed.GetDocument(documentIds[i])!, Microsoft.CodeAnalysis.Formatting.Formatter.Annotation);
            fixedSources[i] = (await document.GetTextAsync()).ToString();
            if (Environment.GetEnvironmentVariable("KEEL_PRINT_FIXES") is "1")
                Console.WriteLine($"--- {sources[i].Name} after {id} fix ---\n{fixedSources[i]}");
        }

        var compilation = (await changed.GetProject(project.Id)!.GetCompilationAsync())!;
        var errors = compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        if (errors.Count > 0) throw new AssertionException("fixed code does not compile: " + string.Join("; ", errors));

        var remaining = (await DiagnosticsAsync(changed.GetProject(project.Id)!)).Select(d => d.Id).Where(d => d == id);
        return (fixedSources, string.Join(",", remaining));
    }

    private static async Task<ImmutableArray<Diagnostic>> DiagnosticsAsync(Project project)
    {
        var compilation = (await project.GetCompilationAsync())!;
        return await compilation
            .WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new EndpointAnalyzer()))
            .GetAnalyzerDiagnosticsAsync();
    }

    private static IEnumerable<MetadataReference> References() =>
        ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Where(p => p.EndsWith(".dll", StringComparison.Ordinal))
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p))
            .Append(MetadataReference.CreateFromFile(typeof(EndpointBase).Assembly.Location));
}
