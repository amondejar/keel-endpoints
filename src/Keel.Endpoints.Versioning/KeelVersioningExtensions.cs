using Asp.Versioning;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Keel.Endpoints.Versioning;

/// <summary>Settings for <see cref="KeelVersioningExtensions.WithKeelVersioning"/>.</summary>
public sealed class KeelVersioningOptions
{
    /// <summary>
    /// Endpoints without an <c>[ApiVersion]</c>, <c>[MapToApiVersion]</c> or <c>[ApiVersionNeutral]</c> take their version
    /// from their namespace: <c>Shop.Features.V2.Orders.Get</c> is version 2.0 (<c>v2</c>, <c>V2_1</c>, <c>v2026_10_01</c>
    /// as Asp.Versioning reads them). On by default.
    /// </summary>
    public bool VersionFromNamespace { get; set; } = true;

    /// <summary>
    /// Endpoints that serve exactly one version get it in their operation name (<c>OrdersGet</c> becomes
    /// <c>OrdersGetV2</c>), so two versions of an operation do not collide: ASP.NET Core requires unique endpoint
    /// names, and OpenAPI clients use them as method names. On by default.
    /// </summary>
    public bool VersionedOperationNames { get; set; } = true;

    /// <summary>How namespaces are read. <see cref="NamespaceParser.Default"/> unless set.</summary>
    public NamespaceParser NamespaceParser { get; set; } = NamespaceParser.Default;
}

/// <summary>Keel.Endpoints on an Asp.Versioning versioned API.</summary>
public static class KeelVersioningExtensions
{
    /// <summary>
    /// Applies Keel's versioning conventions to the endpoints under this builder, typically the group returned by
    /// <c>app.NewVersionedApi().MapKeelEndpoints("/api/v{version:apiVersion}")</c>: versions from namespaces and
    /// versioned operation names. Versions on the class (<c>[ApiVersion(2.0)]</c>) or set in <c>Configure</c>
    /// (<c>options.Convention(b =&gt; b.HasApiVersion(2.0))</c>) win over the namespace.
    /// </summary>
    public static TBuilder WithKeelVersioning<TBuilder>(this TBuilder builder, Action<KeelVersioningOptions>? configure = null)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        var options = new KeelVersioningOptions();
        configure?.Invoke(options);

        // Finally: after each endpoint's own conventions (its class attributes and Configure), and before the
        // versioned API's finalizer on the enclosing group turns the declared versions into ApiVersionMetadata.
        builder.Finally(endpoint =>
        {
            if (endpoint.Metadata.OfType<KeelEndpointMetadata>().LastOrDefault() is not { } keel) return;

            if (endpoint.Metadata.OfType<ApiVersionMetadata>().Any())
            {
                throw new InvalidOperationException(
                    $"WithKeelVersioning() ran after the versioned API's own conventions for {keel.EndpointType.FullName}. Call it on " +
                    "a group inside the versioned API: app.NewVersionedApi().MapKeelEndpoints(\"/api/v{version:apiVersion}\").WithKeelVersioning().");
            }

            var declared = endpoint.Metadata.OfType<IApiVersionProvider>().ToList();
            var neutral = endpoint.Metadata.OfType<IApiVersionNeutral>().Any();

            if (options.VersionFromNamespace && declared.Count == 0 && !neutral
                && options.NamespaceParser.Parse(keel.EndpointType) is { Count: > 0 } fromNamespace)
            {
                var provider = new NamespaceApiVersions(fromNamespace);
                endpoint.Metadata.Add(provider);
                declared.Add(provider);
            }

            if (!options.VersionedOperationNames || neutral) return;

            var versions = declared
                .Where(p => (p.Options & ApiVersionProviderOptions.Advertised) == 0)
                .SelectMany(p => p.Versions)
                .Distinct()
                .ToList();
            if (versions.Count != 1) return;

            for (var i = 0; i < endpoint.Metadata.Count; i++)
            {
                switch (endpoint.Metadata[i])
                {
                    case IEndpointNameMetadata name:
                        endpoint.Metadata[i] = new EndpointNameMetadata(name.EndpointName + Suffix(versions[0]));
                        break;
                    case IRouteNameMetadata route when route.RouteName is { } routeName:
                        endpoint.Metadata[i] = new RouteNameMetadata(routeName + Suffix(versions[0]));
                        break;
                }
            }
        });
        return builder;
    }

    /// <summary><c>V2</c>, <c>V2_1</c>, <c>V2026_10_01</c>, <c>V2Beta</c>: letters, digits and underscores only.</summary>
    internal static string Suffix(ApiVersion version)
    {
        var text = new System.Text.StringBuilder("V");
        if (version.GroupVersion is { } date)
        {
            text.Append(date.ToString("yyyy_MM_dd", System.Globalization.CultureInfo.InvariantCulture));
            if (version.MajorVersion is not null) text.Append('_');
        }

        if (version.MajorVersion is { } major)
        {
            text.Append(major);
            if (version.MinorVersion is { } minor and > 0) text.Append('_').Append(minor);
        }

        if (!string.IsNullOrEmpty(version.Status))
        {
            foreach (var c in version.Status)
                if (char.IsLetterOrDigit(c)) text.Append(text.Length == 1 || !char.IsLetter(text[^1]) ? char.ToUpperInvariant(c) : c);
        }

        return text.ToString();
    }

    /// <summary>The versions read from an endpoint's namespace, declared like <c>[ApiVersion]</c>.</summary>
    private sealed class NamespaceApiVersions(IReadOnlyList<ApiVersion> versions) : IApiVersionProvider
    {
        public ApiVersionProviderOptions Options => ApiVersionProviderOptions.None;

        public IReadOnlyList<ApiVersion> Versions { get; } = versions;
    }
}
