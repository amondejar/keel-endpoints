using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;

namespace Keel.Endpoints.Analyzers;

/// <summary>
/// The API versions an endpoint class declares, as KEEL001 needs them: from Asp.Versioning's <c>[ApiVersion]</c>,
/// <c>[MapToApiVersion]</c> and <c>[ApiVersionNeutral]</c> on the class, or else from a version segment of its
/// namespace (<c>V2</c>, <c>v2_1</c>, <c>v2026_10_01</c>), the way Keel.Endpoints.Versioning reads it.
/// Versions set by conventions (an EndpointGroup, Configure) are not visible here.
/// </summary>
internal static class ApiVersions
{
    private const string Neutral = "*";
    private static readonly Regex MajorMinor = new(@"^[vV_](\d{1,7})(?:_(\d+))?$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex Date = new(@"^[vV_](\d{4})_?(\d{2})_?(\d{2})$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex Number = new(@"^(\d+)(?:\.(\d+))?$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static HashSet<string> Of(INamedTypeSymbol type)
    {
        var versions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var attribute in type.GetAttributes())
        {
            var attributeClass = attribute.AttributeClass;
            if (attributeClass?.ContainingNamespace?.ToDisplayString() != "Asp.Versioning") continue;

            switch (attributeClass.Name)
            {
                case "ApiVersionNeutralAttribute":
                    versions.Add(Neutral);
                    break;
                case "ApiVersionAttribute" or "MapToApiVersionAttribute":
                    foreach (var argument in attribute.ConstructorArguments)
                    {
                        if (argument.Kind == TypedConstantKind.Array)
                        {
                            foreach (var item in argument.Values) Add(versions, item.Value);
                        }
                        else
                        {
                            Add(versions, argument.Value);
                        }
                    }

                    break;
            }
        }

        if (versions.Count > 0) return versions;

        foreach (var segment in (type.ContainingNamespace?.ToDisplayString() ?? string.Empty).Split('.'))
        {
            var date = Date.Match(segment);
            if (date.Success)
            {
                versions.Add($"{date.Groups[1].Value}-{date.Groups[2].Value}-{date.Groups[3].Value}");
                continue;
            }

            var number = MajorMinor.Match(segment);
            if (number.Success) versions.Add($"{int.Parse(number.Groups[1].Value, CultureInfo.InvariantCulture)}.{(number.Groups[2].Success ? int.Parse(number.Groups[2].Value, CultureInfo.InvariantCulture) : 0)}");
        }

        return versions;
    }

    /// <summary>Two endpoints can answer the same request unless both declare versions and none is shared.</summary>
    public static bool Overlap(HashSet<string> a, HashSet<string> b) =>
        a.Count == 0 || b.Count == 0 || a.Contains(Neutral) || b.Contains(Neutral) || a.Overlaps(b);

    private static void Add(HashSet<string> versions, object? value)
    {
        switch (value)
        {
            case double d:
                versions.Add(Normalize(d.ToString("0.0###", CultureInfo.InvariantCulture)));
                break;
            case int i:
                versions.Add($"{i}.0");
                break;
            case string s:
                versions.Add(Normalize(s));
                break;
        }
    }

    private static string Normalize(string text)
    {
        var match = Number.Match(text.Trim());
        return match.Success
            ? $"{int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture)}.{(match.Groups[2].Success ? int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture) : 0)}"
            : text.Trim();
    }
}
