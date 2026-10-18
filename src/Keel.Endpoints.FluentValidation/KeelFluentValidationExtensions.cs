using System.Reflection;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Keel.Endpoints.FluentValidation;

/// <summary>Registration and endpoint conventions for FluentValidation.</summary>
public static class KeelFluentValidationExtensions
{
    /// <summary>
    /// Registers every concrete <see cref="IValidator{T}"/> in <paramref name="assembly"/>.
    /// Validators registered any other way, for example with
    /// <c>FluentValidation.DependencyInjectionExtensions</c>, work just as well.
    /// </summary>
    public static IServiceCollection AddKeelFluentValidation(
        this IServiceCollection services, Assembly assembly, ServiceLifetime lifetime = ServiceLifetime.Scoped)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        var validatorTypes = assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false, IsGenericTypeDefinition: false })
            .OrderBy(t => t.FullName, StringComparer.Ordinal);

        foreach (var type in validatorTypes)
        {
            foreach (var contract in type.GetInterfaces())
            {
                if (contract.IsGenericType && contract.GetGenericTypeDefinition() == typeof(IValidator<>))
                    services.TryAddEnumerable(ServiceDescriptor.Describe(contract, type, lifetime));
            }
        }

        return services;
    }

    /// <summary>
    /// Validates endpoint arguments with their registered validators before the handler runs.
    /// Apply it to a route group (<c>app.MapKeelEndpoints(...).WithFluentValidation()</c>), to a single
    /// endpoint (<c>options.Convention(b => b.WithFluentValidation())</c>) or to any Minimal API.
    /// Failures return <c>400</c> validation problem details, the same shape as .NET 10 validation.
    /// </summary>
    public static TBuilder WithFluentValidation<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder
    {
        builder.AddEndpointFilterFactory(FluentValidationFilter.Create);
        return builder;
    }
}
