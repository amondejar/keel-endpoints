using System.Reflection;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Validation;

namespace Keel.Endpoints.Internal;

/// <summary>Metadata that makes the OpenAPI document describe Keel endpoints accurately.</summary>
internal static class OpenApiConventions
{
    /// <summary>
    /// The tag for an endpoint without one. A class named <c>Endpoint</c> lives in its operation's
    /// namespace (<c>Invoices.Get</c>), so the feature is the segment before it; any other class
    /// lives in its feature's namespace, so the feature is the last segment.
    /// </summary>
    public static string? DefaultTag(Type endpointType)
    {
        var segments = (endpointType.Namespace ?? string.Empty).Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0) return null;
        return endpointType.Name == "Endpoint" && segments.Length > 1 ? segments[^2] : segments[^1];
    }

    /// <summary>
    /// True when .NET 10 validation is on (<c>AddValidation()</c>) and the contract, or a <c>[FromBody]</c>
    /// member of it, is marked <c>[ValidatableType]</c>: the cases where the endpoint can answer 400.
    /// </summary>
    public static bool UsesNativeValidation(IServiceProvider services, Type? requestType)
    {
        if (requestType is null) return false;
        // Resolvers is marked experimental (ASP0029) in .NET 10; it is the only public signal that
        // AddValidation() was called. If it changes, this check is the one line to update.
#pragma warning disable ASP0029
        if (services.GetService<IOptions<ValidationOptions>>()?.Value.Resolvers.Count is not > 0) return false;
#pragma warning restore ASP0029

        if (IsValidatable(requestType)) return true;
        return requestType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Any(p => p.GetCustomAttributes(inherit: true).OfType<IFromBodyMetadata>().Any() && IsValidatable(p.PropertyType));
    }

    // Matched by name: the attribute is marked experimental in .NET 10.
    private static bool IsValidatable(Type type) =>
        type.GetCustomAttributes(inherit: false).Any(a => a.GetType().Name == "ValidatableTypeAttribute");
}
