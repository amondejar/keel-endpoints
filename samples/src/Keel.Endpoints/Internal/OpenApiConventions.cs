using System.Diagnostics.CodeAnalysis;
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
    /// True when .NET 10 validation is on (<c>AddValidation()</c>) and its resolvers know the contract, or a
    /// <c>[FromBody]</c> member of it: the cases where the endpoint can answer 400.
    /// </summary>
    public static bool UsesNativeValidation(
        IServiceProvider services,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] Type? requestType)
    {
        if (requestType is null) return false;
        if (services.GetService<IOptions<ValidationOptions>>()?.Value is not { } validation) return false;

        // Ask the validation resolvers (what AddValidation() registered) rather than looking for
        // [ValidatableType]: in a generated registration the .NET 10 generator finds contracts without it.
        if (IsValidatable(validation, requestType)) return true;
        return requestType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Any(p => p.GetCustomAttributes(inherit: true).OfType<IFromBodyMetadata>().Any() && IsValidatable(validation, p.PropertyType));
    }

    // The validation resolver API is marked experimental (ASP0029) in .NET 10.
#pragma warning disable ASP0029
    private static bool IsValidatable(ValidationOptions validation, Type type) =>
        validation.TryGetValidatableTypeInfo(type, out _);
#pragma warning restore ASP0029
}
