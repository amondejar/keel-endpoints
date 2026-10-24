using System.ComponentModel.DataAnnotations;
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
    /// lives in its feature's namespace, so the feature is the last segment. A single-operation feature
    /// directly in a container folder (<c>Features.Health</c>, <c>Features.V2.Health</c>) is its own tag.
    /// </summary>
    public static string? DefaultTag(Type endpointType)
    {
        var segments = (endpointType.Namespace ?? string.Empty).Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0) return null;
        return endpointType.Name == "Endpoint" && segments.Length > 1 && !EndpointNames.IsContainer(segments[^2]) ? segments[^2] : segments[^1];
    }

    /// <summary>
    /// True when .NET 10 validation is on (<c>AddValidation()</c>), its resolvers know the contract (or a
    /// <c>[FromBody]</c> member of it), and there is a rule to check: the cases where the endpoint can answer 400.
    /// </summary>
    public static bool UsesNativeValidation(
        IServiceProvider services,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicConstructors)] Type? requestType)
    {
        if (requestType is null) return false;
        if (services.GetService<IOptions<ValidationOptions>>()?.Value is not { } validation) return false;

        // Ask the validation resolvers (what AddValidation() registered) rather than looking for
        // [ValidatableType]: in a generated registration the .NET 10 generator finds contracts without it.
        // The resolvers also know types with nothing to validate (the generator emits every complex type it
        // sees in a Map call, and under Native AOT it sees every Keel contract), so look for a rule as well.
        var properties = requestType.GetProperties(BindingFlags.Public | BindingFlags.Instance);
        if (Knows(validation, requestType) && HasRules(validation, requestType, properties)) return true;
        return properties.Any(p => p.GetCustomAttributes(inherit: true).OfType<IFromBodyMetadata>().Any() && Knows(validation, p.PropertyType));
    }

    private static bool HasRules(
        ValidationOptions validation,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] Type type,
        PropertyInfo[] properties)
    {
        if (typeof(IValidatableObject).IsAssignableFrom(type) || type.IsDefined(typeof(ValidationAttribute), inherit: true)) return true;
        if (properties.Any(p => p.IsDefined(typeof(ValidationAttribute), inherit: true))) return true;
        // Record parameters: [Required] string Name, without [property:].
        if (type.GetConstructors().SelectMany(c => c.GetParameters()).Any(p => p.IsDefined(typeof(ValidationAttribute), inherit: true))) return true;
        // A member of a validatable type is validated recursively.
        return properties.Any(p => !p.PropertyType.IsPrimitive && p.PropertyType != typeof(string) && Knows(validation, p.PropertyType));
    }

    // The validation resolver API is marked experimental (ASP0029) in .NET 10.
#pragma warning disable ASP0029
    private static bool Knows(ValidationOptions validation, Type type) =>
        validation.TryGetValidatableTypeInfo(type, out _);
#pragma warning restore ASP0029
}
