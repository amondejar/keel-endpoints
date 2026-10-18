using System;
using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Keel.Endpoints.Analyzers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;

namespace Keel.Endpoints.CodeFixes;

/// <summary>KEEL002: adds the missing route parameter to the contract as a <c>string</c> member.</summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(AddContractMemberFix)), Shared]
public sealed class AddContractMemberFix : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds => ImmutableArray.Create("KEEL002");

    public override FixAllProvider? GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        foreach (var diagnostic in context.Diagnostics)
        {
            if (!diagnostic.Properties.TryGetValue(EndpointAnalyzer.ParameterProperty, out var parameter) || parameter is null) continue;
            var member = Contracts.Pascal(parameter);
            context.RegisterCodeFix(
                CodeAction.Create(
                    $"Add '{member}' to the contract",
                    ct => Contracts.ChangeContractAsync(context.Document, diagnostic, (declaration, _) => AddMember(declaration, member), ct),
                    equivalenceKey: "KEEL002:" + member),
                diagnostic);
        }

        return Task.CompletedTask;
    }

    private static TypeDeclarationSyntax AddMember(TypeDeclarationSyntax declaration, string member)
    {
        // A positional record gets a constructor parameter, so the route value binds through it.
        if (declaration is RecordDeclarationSyntax { ParameterList: { } parameters } record)
        {
            var parameter = SyntaxFactory.Parameter(SyntaxFactory.Identifier(member))
                .WithType(SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.StringKeyword)));
            return record.WithParameterList(parameters.AddParameters(parameter));
        }

        var property = (PropertyDeclarationSyntax)SyntaxFactory.ParseMemberDeclaration(
            $"public string {member} {{ get; init; }} = string.Empty;")!;
        property = property.WithLeadingTrivia(SyntaxFactory.ElasticCarriageReturnLineFeed);

        // "record X;" or "class X;" has no body yet.
        if (declaration.OpenBraceToken.IsKind(SyntaxKind.None))
        {
            declaration = declaration
                .WithSemicolonToken(default)
                .WithOpenBraceToken(SyntaxFactory.Token(SyntaxKind.OpenBraceToken))
                .WithCloseBraceToken(SyntaxFactory.Token(SyntaxKind.CloseBraceToken));
        }

        return declaration.AddMembers(property).WithAdditionalAnnotations(Formatter.Annotation);
    }
}

/// <summary>KEEL003: binds the contract from parameters (route, query, headers) instead of the body.</summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(BindAsParametersFix)), Shared]
public sealed class BindAsParametersFix : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds => ImmutableArray.Create("KEEL003");

    public override FixAllProvider? GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        foreach (var diagnostic in context.Diagnostics)
        {
            context.RegisterCodeFix(
                CodeAction.Create(
                    "Bind the contract from parameters ([BindRequest(RequestBinding.Parameters)])",
                    ct => Contracts.ChangeContractAsync(context.Document, diagnostic,
                        (declaration, _) => Contracts.AddAttribute(declaration, "BindRequest(RequestBinding.Parameters)"),
                        ct, requiredUsing: "Keel.Endpoints"),
                    equivalenceKey: "KEEL003"),
                diagnostic);
        }

        return Task.CompletedTask;
    }
}

/// <summary>KEEL004: marks the contract <c>[ValidatableType]</c> so .NET 10 validation runs.</summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(AddValidatableTypeFix)), Shared]
public sealed class AddValidatableTypeFix : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds => ImmutableArray.Create("KEEL004");

    public override FixAllProvider? GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null) return;

        foreach (var diagnostic in context.Diagnostics)
        {
            // KEEL004 is reported on the contract declaration itself.
            var declaration = root.FindToken(diagnostic.Location.SourceSpan.Start).Parent?.AncestorsAndSelf()
                .OfType<TypeDeclarationSyntax>().FirstOrDefault();
            if (declaration is null) continue;

            context.RegisterCodeFix(
                CodeAction.Create(
                    "Add [ValidatableType]",
                    ct =>
                    {
                        var newRoot = root.ReplaceNode(declaration, Contracts.AddAttribute(declaration, "ValidatableType"));
                        newRoot = Contracts.EnsureUsing(newRoot, "Microsoft.Extensions.Validation");
                        return Task.FromResult(context.Document.WithSyntaxRoot(newRoot));
                    },
                    equivalenceKey: "KEEL004"),
                diagnostic);
        }
    }
}

internal static class Contracts
{
    public static string Pascal(string name) =>
        name.Length == 0 ? name : char.ToUpperInvariant(name[0]) + name.Substring(1);

    /// <summary>
    /// Finds the contract named by the diagnostic (it may live in another file than the endpoint) and
    /// rewrites its declaration.
    /// </summary>
    public static async Task<Solution> ChangeContractAsync(
        Document document,
        Diagnostic diagnostic,
        Func<TypeDeclarationSyntax, SyntaxNode, TypeDeclarationSyntax> change,
        CancellationToken ct,
        string? requiredUsing = null)
    {
        var solution = document.Project.Solution;
        if (!diagnostic.Properties.TryGetValue(EndpointAnalyzer.ContractProperty, out var id) || id is null) return solution;

        var compilation = await document.Project.GetCompilationAsync(ct).ConfigureAwait(false);
        if (compilation is null) return solution;

        var contract = DocumentationCommentId.GetFirstSymbolForDeclarationId(id, compilation) as INamedTypeSymbol;
        var reference = contract?.DeclaringSyntaxReferences.FirstOrDefault();
        if (reference is null) return solution;

        var target = solution.GetDocument(reference.SyntaxTree);
        if (target is null) return solution;

        var root = await target.GetSyntaxRootAsync(ct).ConfigureAwait(false);
        if (root is null) return solution;
        if (await reference.GetSyntaxAsync(ct).ConfigureAwait(false) is not TypeDeclarationSyntax declaration) return solution;

        var newRoot = root.ReplaceNode(declaration, change(declaration, root));
        if (requiredUsing is not null) newRoot = EnsureUsing(newRoot, requiredUsing);
        return solution.WithDocumentSyntaxRoot(target.Id, newRoot);
    }

    public static TypeDeclarationSyntax AddAttribute(TypeDeclarationSyntax declaration, string attribute)
    {
        var list = SyntaxFactory.AttributeList(SyntaxFactory.SingletonSeparatedList(
                SyntaxFactory.Attribute(SyntaxFactory.ParseName(attribute.Split('(')[0]))
                    .WithArgumentList(attribute.Contains("(")
                        ? SyntaxFactory.ParseAttributeArgumentList(attribute.Substring(attribute.IndexOf('(')))
                        : null)))
            .WithAdditionalAnnotations(Formatter.Annotation);

        // Keep comments and doc comments above the new attribute.
        var leading = declaration.GetLeadingTrivia();
        return declaration
            .WithoutLeadingTrivia()
            .WithAttributeLists(declaration.AttributeLists.Count == 0
                ? SyntaxFactory.SingletonList(list.WithTrailingTrivia(SyntaxFactory.ElasticCarriageReturnLineFeed))
                : declaration.AttributeLists.Insert(0, list.WithTrailingTrivia(SyntaxFactory.ElasticCarriageReturnLineFeed)))
            .WithLeadingTrivia(leading);
    }

    public static SyntaxNode EnsureUsing(SyntaxNode root, string ns)
    {
        if (root is not CompilationUnitSyntax unit) return root;
        if (unit.Usings.Any(u => u.Name?.ToString() == ns)) return root;
        if (unit.DescendantNodes().OfType<BaseNamespaceDeclarationSyntax>()
            .SelectMany(n => n.Usings).Any(u => u.Name?.ToString() == ns)) return root;

        var directive = SyntaxFactory.UsingDirective(SyntaxFactory.ParseName(ns))
            .WithTrailingTrivia(SyntaxFactory.ElasticCarriageReturnLineFeed);
        return unit.AddUsings(directive).WithAdditionalAnnotations(Formatter.Annotation);
    }
}
