using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Keel.Endpoints.Analyzers;

/// <summary>Build-time checks for Keel.Endpoints endpoint classes.</summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class EndpointAnalyzer : DiagnosticAnalyzer
{
    private const string Category = "Keel.Endpoints";
    private const string HelpBase = "https://github.com/amondejar/keel-endpoints/blob/main/docs/diagnostics.md#";

    public static readonly DiagnosticDescriptor DuplicateRoute = new(
        "KEEL001",
        "Duplicate route",
        "Endpoint '{0}' maps {1} '{2}', which is already mapped by '{3}'",
        Category, DiagnosticSeverity.Warning, isEnabledByDefault: true,
        description: "Two endpoints with the same HTTP method and route template make routing ambiguous at runtime.",
        helpLinkUri: HelpBase + "keel001",
        customTags: WellKnownDiagnosticTags.CompilationEnd);

    public static readonly DiagnosticDescriptor RouteParameterNotInContract = new(
        "KEEL002",
        "Route parameter has no matching contract member",
        "Route parameter '{0}' of endpoint '{1}' has no matching member on contract '{2}'",
        Category, DiagnosticSeverity.Warning, isEnabledByDefault: true,
        description: "A route parameter that no contract member binds is silently ignored.",
        helpLinkUri: HelpBase + "keel002");

    public static readonly DiagnosticDescriptor RouteParameterWithBodyContract = new(
        "KEEL003",
        "Route parameter is not bound because the contract is read from the body",
        "Endpoint '{0}' reads contract '{1}' from the body, so route parameter '{2}' is not bound; add [BindRequest(RequestBinding.Parameters)] to the contract and mark its body member [FromBody]",
        Category, DiagnosticSeverity.Warning, isEnabledByDefault: true,
        description: "POST, PUT and PATCH contracts are read from the JSON body by default, which leaves route values unbound.",
        helpLinkUri: HelpBase + "keel003");

    public static readonly DiagnosticDescriptor ContractNotValidatable = new(
        "KEEL004",
        "Contract has validation rules but is not marked [ValidatableType]",
        "Contract '{0}' has validation rules, on its members or on a member's type, but is not marked [ValidatableType], so .NET 10 validation will not run for it",
        Category, DiagnosticSeverity.Warning, isEnabledByDefault: true,
        description: "The .NET 10 validation generator only discovers contract types reached through endpoint classes when they carry [ValidatableType].",
        helpLinkUri: HelpBase + "keel004");

    private static readonly Regex RouteParameter = new(@"\{\*{0,2}([A-Za-z_][A-Za-z0-9_]*)[^}]*\}", RegexOptions.Compiled);
    private static readonly Regex AnyRouteParameter = new(@"\{[^}]*\}", RegexOptions.Compiled);

    private static readonly ImmutableHashSet<string> BodyMethods = ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase, "POST", "PUT", "PATCH");

    private static readonly Dictionary<string, string> RouteAttributes = new()
    {
        ["GetAttribute"] = "GET",
        ["PostAttribute"] = "POST",
        ["PutAttribute"] = "PUT",
        ["PatchAttribute"] = "PATCH",
        ["DeleteAttribute"] = "DELETE",
    };

    private static readonly ImmutableHashSet<string> RequestShapes =
        ImmutableHashSet.Create("Endpoint`2", "Endpoint`1", "ResultEndpoint`2", "StreamEndpoint`2");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(DuplicateRoute, RouteParameterNotInContract, RouteParameterWithBodyContract, ContractNotValidatable);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterCompilationStartAction(start =>
        {
            var routes = new ConcurrentBag<(string Key, string Method, string Template, INamedTypeSymbol Type, Location Location, HashSet<string> Versions)>();

            // With the registration written to disk (Native AOT), the .NET 10 validation generator sees every
            // contract in the generated MapMethods calls, so [ValidatableType] is not needed (KEEL004 is off).
            var registrationFile = start.Options.AnalyzerConfigOptionsProvider.GlobalOptions
                .TryGetValue("build_property.KeelRegistrationFile", out var value)
                && string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);

            start.RegisterSymbolAction(symbolContext =>
            {
                var type = (INamedTypeSymbol)symbolContext.Symbol;
                if (type.IsAbstract || type.TypeKind != TypeKind.Class) return;

                var route = FindRoute(type);
                if (route is null) return;
                var (method, template, attribute) = route.Value;
                var location = attribute.ApplicationSyntaxReference?.GetSyntax(symbolContext.CancellationToken).GetLocation()
                               ?? type.Locations.FirstOrDefault() ?? Location.None;

                routes.Add((Normalize(method, template), method, template, type, location, ApiVersions.Of(type)));

                var contract = FindContract(type);
                if (contract is null) return;

                CheckRouteParameters(symbolContext, type, contract, method, template, location);
                if (!registrationFile) CheckValidatable(symbolContext, contract);
            }, SymbolKind.NamedType);

            start.RegisterCompilationEndAction(end =>
            {
                foreach (var group in routes.GroupBy(r => r.Key).Where(g => g.Count() > 1))
                {
                    var ordered = group.OrderBy(r => r.Type.ToDisplayString(), StringComparer.Ordinal).ToList();
                    for (var i = 1; i < ordered.Count; i++)
                    {
                        // The same route in two API versions (V1 and V2 folders, or [ApiVersion]) is not a duplicate.
                        var duplicate = ordered[i];
                        var first = ordered.Take(i).FirstOrDefault(r => ApiVersions.Overlap(r.Versions, duplicate.Versions));
                        if (first.Type is null) continue;
                        end.ReportDiagnostic(Diagnostic.Create(DuplicateRoute, duplicate.Location,
                            duplicate.Type.Name, duplicate.Method, duplicate.Template, first.Type.ToDisplayString()));
                    }
                }
            });
        });
    }

    private static (string Method, string Template, AttributeData Attribute)? FindRoute(INamedTypeSymbol type)
    {
        foreach (var attribute in type.GetAttributes())
        {
            var attributeClass = attribute.AttributeClass;
            if (attributeClass is null || attributeClass.ContainingNamespace?.ToDisplayString() != "Keel.Endpoints") continue;
            if (!RouteAttributes.TryGetValue(attributeClass.Name, out var method)) continue;
            if (attribute.ConstructorArguments.Length != 1 || attribute.ConstructorArguments[0].Value is not string template) continue;
            return (method, template, attribute);
        }

        return null;
    }

    private static INamedTypeSymbol? FindContract(INamedTypeSymbol type)
    {
        for (var baseType = type.BaseType; baseType is not null; baseType = baseType.BaseType)
        {
            if (baseType.ContainingNamespace?.ToDisplayString() != "Keel.Endpoints") continue;
            if (!RequestShapes.Contains(baseType.MetadataName)) continue;
            return baseType.TypeArguments.FirstOrDefault() as INamedTypeSymbol;
        }

        return null;
    }

    private static void CheckRouteParameters(SymbolAnalysisContext context, INamedTypeSymbol endpoint, INamedTypeSymbol contract,
        string method, string template, Location location)
    {
        var parameters = RouteParameter.Matches(template).Cast<Match>().Select(m => m.Groups[1].Value).ToList();
        if (parameters.Count == 0) return;

        if (ReadsFromBody(contract, method))
        {
            context.ReportDiagnostic(Diagnostic.Create(RouteParameterWithBodyContract, location,
                FixProperties(contract, parameters[0]), endpoint.Name, contract.Name, parameters[0]));
            return;
        }

        var members = new HashSet<string>(
            contract.GetMembers().OfType<IPropertySymbol>().Select(p => p.Name)
                .Concat(contract.InstanceConstructors.SelectMany(c => c.Parameters).Select(p => p.Name)),
            StringComparer.OrdinalIgnoreCase);

        foreach (var parameter in parameters.Where(p => !members.Contains(p)))
        {
            context.ReportDiagnostic(Diagnostic.Create(RouteParameterNotInContract, location,
                FixProperties(contract, parameter), parameter, endpoint.Name, contract.Name));
        }
    }

    /// <summary>What the code fixes need: which contract to change and which route parameter it is about.</summary>
    private static ImmutableDictionary<string, string?> FixProperties(INamedTypeSymbol contract, string parameter) =>
        ImmutableDictionary<string, string?>.Empty
            .Add(ContractProperty, DocumentationCommentId.CreateDeclarationId(contract))
            .Add(ParameterProperty, parameter);

    public const string ContractProperty = "KeelContract";
    public const string ParameterProperty = "KeelParameter";

    private static bool ReadsFromBody(INamedTypeSymbol contract, string method)
    {
        foreach (var attribute in contract.GetAttributes())
        {
            if (attribute.AttributeClass?.Name == "BindRequestAttribute"
                && attribute.AttributeClass.ContainingNamespace?.ToDisplayString() == "Keel.Endpoints"
                && attribute.ConstructorArguments.Length == 1
                && attribute.ConstructorArguments[0].Value is int binding)
            {
                return binding == 1; // RequestBinding.Body
            }
        }

        return BodyMethods.Contains(method);
    }

    private static void CheckValidatable(SymbolAnalysisContext context, INamedTypeSymbol contract)
    {
        if (contract.Locations.All(l => !l.IsInSource)) return;
        if (contract.GetAttributes().Any(a => a.AttributeClass?.Name == "ValidatableTypeAttribute")) return;

        var properties = contract.GetMembers().OfType<IPropertySymbol>().ToList();
        var usesValidation =
            properties.SelectMany(p => p.GetAttributes())
                .Concat(contract.InstanceConstructors.SelectMany(c => c.Parameters).SelectMany(p => p.GetAttributes()))
                .Any(a => InheritsValidationAttribute(a.AttributeClass));

        // A contract that only wraps a validatable type (a [FromBody] member, say) must be marked
        // too, or validation stops at the contract and never reaches the member.
        var wrapsValidatable = properties.Any(p =>
            p.Type.GetAttributes().Any(a => a.AttributeClass?.Name == "ValidatableTypeAttribute"));

        if (!usesValidation && !wrapsValidatable) return;

        context.ReportDiagnostic(Diagnostic.Create(ContractNotValidatable, contract.Locations.First(), contract.Name));
    }

    private static bool InheritsValidationAttribute(INamedTypeSymbol? attributeClass)
    {
        for (var t = attributeClass; t is not null; t = t.BaseType)
        {
            if (t.Name == "ValidationAttribute" && t.ContainingNamespace?.ToDisplayString() == "System.ComponentModel.DataAnnotations")
                return true;
        }

        return false;
    }

    private static string Normalize(string method, string template)
    {
        var normalized = AnyRouteParameter.Replace(template.Trim('/'), "{}").ToLowerInvariant();
        return method + " " + normalized;
    }
}
