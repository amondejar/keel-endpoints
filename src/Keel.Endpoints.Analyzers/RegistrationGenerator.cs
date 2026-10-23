using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Keel.Endpoints.Analyzers;

/// <summary>
/// Generates <c>app.MapKeelEndpoints(prefix)</c>: one explicit, typed <c>MapMethods</c> call per endpoint
/// class, with no reflection. Under Native AOT the build writes this file to disk before compiling
/// (see build/Keel.Endpoints.targets) so ASP.NET Core's Request Delegate Generator can see the calls
/// and generate AOT-safe binding for them.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class RegistrationGenerator : IIncrementalGenerator
{
    /// <summary>The generated class; if it is already in the compilation (the AOT file), emit nothing.</summary>
    public const string GeneratedTypeName = "Microsoft.AspNetCore.Builder.KeelGeneratedEndpointRouteBuilderExtensions";
    public const string HintName = "KeelEndpoints.g.cs";

    private static readonly string[] RouteAttributes =
        ["Keel.Endpoints.GetAttribute", "Keel.Endpoints.PostAttribute", "Keel.Endpoints.PutAttribute", "Keel.Endpoints.PatchAttribute", "Keel.Endpoints.DeleteAttribute"];

    public static readonly DiagnosticDescriptor NotAccessible = new(
        "KEEL005",
        "Endpoint class cannot be registered by the generated code",
        "Endpoint '{0}' is {1}, so the generated MapKeelEndpoints() cannot register it; make it public or internal and not nested in a private type",
        "Keel.Endpoints", DiagnosticSeverity.Warning, isEnabledByDefault: true,
        helpLinkUri: "https://github.com/TU-USUARIO/keel-endpoints/blob/main/docs/diagnostics.md#keel005");

    public static readonly DiagnosticDescriptor GroupNotUsable = new(
        "KEEL006",
        "Endpoint group cannot be used by the generated code",
        "Endpoint group '{0}' {1}, so the generated MapKeelEndpoints() cannot create it; give it a public parameterless constructor and make it public or internal",
        "Keel.Endpoints", DiagnosticSeverity.Warning, isEnabledByDefault: true,
        helpLinkUri: "https://github.com/TU-USUARIO/keel-endpoints/blob/main/docs/diagnostics.md#keel006");

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var perAttribute = RouteAttributes.Select(name =>
            context.SyntaxProvider.ForAttributeWithMetadataName(name, static (node, _) => true, static (ctx, ct) => Describe(ctx, ct)).Collect());

        var all = perAttribute.Aggregate(
            context.CompilationProvider.Select(static (_, _) => ImmutableArray<EndpointModel>.Empty),
            (acc, next) => acc.Combine(next).Select(static (pair, _) => pair.Left.AddRange(pair.Right.Where(m => m is not null)!)));

        var groups = context.SyntaxProvider.CreateSyntaxProvider(
                static (node, _) => node is Microsoft.CodeAnalysis.CSharp.Syntax.ClassDeclarationSyntax { BaseList: not null },
                static (ctx, ct) => DescribeGroup(ctx, ct))
            .Where(static g => g is not null)
            .Collect();

        var alreadyInSource = context.CompilationProvider.Select(static (compilation, _) =>
            compilation.GetTypeByMetadataName(GeneratedTypeName) is { } type && type.Locations.Any(l => l.IsInSource));

        context.RegisterSourceOutput(all.Combine(groups).Combine(alreadyInSource), static (spc, input) =>
        {
            var ((endpoints, groupModels), inSource) = input;
            foreach (var endpoint in endpoints.Where(e => e.Problem is not null))
                spc.ReportDiagnostic(Diagnostic.Create(NotAccessible, endpoint.Location?.ToLocation() ?? Location.None, endpoint.DisplayName, endpoint.Problem));
            foreach (var group in groupModels.Where(g => g!.Problem is not null))
                spc.ReportDiagnostic(Diagnostic.Create(GroupNotUsable, group!.Location?.ToLocation() ?? Location.None, group.DisplayName, group.Problem));

            if (inSource) return; // the build already compiles the written-out file (Native AOT)
            var usable = groupModels.Where(g => g!.Problem is null).Select(g => g!).ToList();
            spc.AddSource(HintName, SourceText.From(Emit(endpoints.Where(e => e.Problem is null), usable), Encoding.UTF8));
        });
    }

    private static EndpointModel? Describe(GeneratorAttributeSyntaxContext ctx, CancellationToken ct)
    {
        if (ctx.TargetSymbol is not INamedTypeSymbol type || type.IsAbstract || type.IsGenericType) return null;

        var attribute = ctx.Attributes.FirstOrDefault();
        if (attribute?.AttributeClass is null || attribute.ConstructorArguments.Length != 1 || attribute.ConstructorArguments[0].Value is not string template)
            return null;

        var method = attribute.AttributeClass.Name.Substring(0, attribute.AttributeClass.Name.Length - "Attribute".Length).ToUpperInvariant();
        var display = type.ToDisplayString();
        var location = LocationInfo.From(type.Locations.FirstOrDefault());

        var problem = AccessibilityProblem(type);
        var endpointNamespace = type.ContainingNamespace is { IsGlobalNamespace: false } ns ? ns.ToDisplayString() : "";
        if (problem is not null) return new EndpointModel(display, "", endpointNamespace, method, template, Shape.Response, null, null, false, false, problem, location);

        var (shape, args) = ShapeOf(type);
        if (shape is null) return null;

        string? request = null, response = null;
        var body = false;
        if (shape != Shape.WithoutRequest)
        {
            var requestType = args[0];
            request = requestType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            body = ReadsFromBody(requestType, method);
        }

        if (args.Length > 1) response = args[1].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var sse = type.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == "Keel.Endpoints.ServerSentEventsAttribute");

        return new EndpointModel(display, type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), endpointNamespace, method, template,
            shape.Value, request, response, body, sse, null, location);
    }

    private static GroupModel? DescribeGroup(GeneratorSyntaxContext ctx, CancellationToken ct)
    {
        if (ctx.SemanticModel.GetDeclaredSymbol(ctx.Node, ct) is not INamedTypeSymbol type || type.IsAbstract || type.IsGenericType) return null;

        var isGroup = false;
        for (var b = type.BaseType; b is not null && !isGroup; b = b.BaseType)
            isGroup = b.ToDisplayString() == "Keel.Endpoints.EndpointGroup";
        if (!isGroup) return null;

        var problem = AccessibilityProblem(type) is { } access ? "is " + access
            : type.InstanceConstructors.Any(c => c.Parameters.Length == 0 && c.DeclaredAccessibility == Accessibility.Public) ? null
            : "has no public parameterless constructor";

        return new GroupModel(type.ToDisplayString(), type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            type.ContainingNamespace is { IsGlobalNamespace: false } ns ? ns.ToDisplayString() : "",
            problem, LocationInfo.From(type.Locations.FirstOrDefault()));
    }

    private static bool Covers(string groupNamespace, string endpointNamespace) =>
        groupNamespace.Length == 0 || endpointNamespace == groupNamespace || endpointNamespace.StartsWith(groupNamespace + ".", StringComparison.Ordinal);

    private static string? AccessibilityProblem(INamedTypeSymbol type)
    {
        for (ISymbol? s = type; s is INamedTypeSymbol t; s = t.ContainingType)
        {
            if (t.DeclaredAccessibility is Accessibility.Private or Accessibility.Protected or Accessibility.ProtectedAndInternal)
                return t.Equals(type, SymbolEqualityComparer.Default) ? "private or protected" : "nested in a private or protected type";
        }

        return null;
    }

    private static (Shape?, ImmutableArray<ITypeSymbol>) ShapeOf(INamedTypeSymbol type)
    {
        for (var b = type.BaseType; b is not null; b = b.BaseType)
        {
            if (b.ContainingNamespace?.ToDisplayString() != "Keel.Endpoints") continue;
            Shape? shape = b.MetadataName switch
            {
                "Endpoint`2" => Shape.Response,
                "Endpoint`1" => Shape.NoContent,
                "EndpointWithoutRequest`1" => Shape.WithoutRequest,
                "ResultEndpoint`2" => Shape.Result,
                "StreamEndpoint`2" => Shape.Stream,
                _ => null,
            };
            if (shape is not null) return (shape, b.TypeArguments);
        }

        return (null, ImmutableArray<ITypeSymbol>.Empty);
    }

    private static bool ReadsFromBody(ITypeSymbol requestType, string method)
    {
        foreach (var attribute in requestType.GetAttributes())
        {
            if (attribute.AttributeClass?.ToDisplayString() == "Keel.Endpoints.BindRequestAttribute"
                && attribute.ConstructorArguments.Length == 1 && attribute.ConstructorArguments[0].Value is int binding)
                return binding == 1; // RequestBinding.Body
        }

        return method is "POST" or "PUT" or "PATCH";
    }

    private static string Emit(IEnumerable<EndpointModel> endpoints, IReadOnlyList<GroupModel> groups)
    {
        const string Http = "global::Microsoft.AspNetCore.Http";
        const string Gen = "global::Keel.Endpoints.Infrastructure.GeneratedEndpoints";

        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("// Generated by Keel.Endpoints: one explicit registration per endpoint class, no reflection.");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        sb.AppendLine("namespace Microsoft.AspNetCore.Builder");
        sb.AppendLine("{");
        sb.AppendLine("    [global::System.CodeDom.Compiler.GeneratedCode(\"Keel.Endpoints\", null)]");
        sb.AppendLine("    internal static class KeelGeneratedEndpointRouteBuilderExtensions");
        sb.AppendLine("    {");
        sb.AppendLine("        /// <summary>");
        sb.AppendLine("        /// Maps every endpoint class in this project under <paramref name=\"prefix\"/>, registered explicitly at");
        sb.AppendLine("        /// compile time: no reflection, safe for trimming and Native AOT. Returns the route group.");
        sb.AppendLine("        /// </summary>");
        sb.AppendLine("        public static global::Microsoft.AspNetCore.Routing.RouteGroupBuilder MapKeelEndpoints(");
        sb.AppendLine("            this global::Microsoft.AspNetCore.Routing.IEndpointRouteBuilder app, string prefix = \"\")");
        sb.AppendLine("        {");
        sb.AppendLine("            var group = app.MapGroup(prefix);");

        foreach (var e in endpoints.OrderBy(e => e.TypeName, StringComparer.Ordinal))
        {
            // The request's IServiceProvider rather than HttpContext: with an endpoint filter (validation, say),
            // each extra argument costs every request, and an HttpContext argument costs ~200 bytes (see benchmarks/).
            var activate = $"{Gen}.Activate<{e.TypeName}>(services)";
            var services = "[global::Microsoft.AspNetCore.Mvc.FromServices] global::System.IServiceProvider services, global::System.Threading.CancellationToken cancellationToken";
            var parameters = e.Shape == Shape.WithoutRequest
                ? services
                : $"[{(e.Body ? "global::Microsoft.AspNetCore.Mvc.FromBody" : Http + ".AsParameters")}] {e.Request} request, {services}";

            string lambda = e.Shape switch
            {
                Shape.WithoutRequest => $"static ({parameters}) => {activate}.HandleAsync(cancellationToken)",
                Shape.NoContent => $"static async ({parameters}) => {{ await {activate}.HandleAsync(request, cancellationToken); return {Http}.TypedResults.NoContent(); }}",
                Shape.Stream when e.ServerSentEvents => $"static ({parameters}) => {Http}.TypedResults.ServerSentEvents({activate}.HandleAsync(request, cancellationToken), {Gen}.EventType<{e.TypeName}>())",
                _ => $"static ({parameters}) => {activate}.HandleAsync(request, cancellationToken)",
            };

            var requestType = e.Request is null ? "null" : $"typeof({e.Request})";
            var binding = e.Shape == Shape.WithoutRequest ? "null" : e.Body ? "global::Keel.Endpoints.RequestBinding.Body" : "global::Keel.Endpoints.RequestBinding.Parameters";
            var covering = groups.Where(g => Covers(g.Namespace, e.Namespace))
                .OrderBy(g => g.Namespace.Length).ThenBy(g => g.TypeName, StringComparer.Ordinal)
                .Select(g => $"new {g.TypeName}()").ToList();
            var groupArray = covering.Count == 0
                ? "global::System.Array.Empty<global::Keel.Endpoints.EndpointGroup>()"
                : $"new global::Keel.Endpoints.EndpointGroup[] {{ {string.Join(", ", covering)} }}";

            sb.AppendLine();
            sb.AppendLine($"            // {e.DisplayName}");
            sb.AppendLine($"            {Gen}.Apply<{e.TypeName}>(");
            sb.AppendLine($"                group.MapMethods({Literal(e.Template)}, new[] {{ {Literal(e.Method)} }},");
            sb.AppendLine($"                    {lambda}),");
            sb.AppendLine($"                group, {requestType}, {binding}, {(e.ServerSentEvents ? "true" : "false")},");
            sb.AppendLine($"                {groupArray});");
        }

        sb.AppendLine();
        sb.AppendLine("            return group;");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine("}");
        return sb.ToString();
    }

    private static string Literal(string value) =>
        "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    private enum Shape { Response, NoContent, WithoutRequest, Result, Stream }

    /// <summary>Plain data only, so the incremental pipeline can cache it.</summary>
    private sealed record EndpointModel(
        string DisplayName, string TypeName, string Namespace, string Method, string Template, Shape Shape,
        string? Request, string? Response, bool Body, bool ServerSentEvents, string? Problem, LocationInfo? Location);

    private sealed record GroupModel(string DisplayName, string TypeName, string Namespace, string? Problem, LocationInfo? Location);

    private sealed record LocationInfo(string Path, TextSpan Span, LinePositionSpan Lines)
    {
        public static LocationInfo? From(Location? location) =>
            location is { SourceTree: not null } ? new LocationInfo(location.SourceTree.FilePath, location.SourceSpan, location.GetLineSpan().Span) : null;

        public Location ToLocation() => Location.Create(Path, Span, Lines);
    }
}
