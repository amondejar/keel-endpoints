using System.Text.RegularExpressions;

namespace Keel.Endpoints.Internal;

/// <summary>Namespace segments that group features rather than name one: <c>Features</c> and API version folders.</summary>
internal static partial class EndpointNames
{
    public static bool IsContainer(string segment) =>
        string.Equals(segment, "Features", StringComparison.Ordinal) || VersionSegment().IsMatch(segment);

    // V2, v2_1, V2026_10_01, _2 (as Asp.Versioning's NamespaceParser reads them).
    [GeneratedRegex("^[vV_][0-9]+(?:_[0-9]+)*$")]
    private static partial Regex VersionSegment();
}
