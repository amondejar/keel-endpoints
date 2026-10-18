using System.Reflection;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.Extensions.DependencyInjection;

namespace Keel.Endpoints.FluentValidation;

/// <summary>
/// Works out once, per endpoint, which arguments have a registered validator. Endpoints with none
/// get no filter at all, so the cost is zero where FluentValidation is not used.
/// </summary>
internal static class FluentValidationFilter
{
    public static EndpointFilterDelegate Create(EndpointFilterFactoryContext context, EndpointFilterDelegate next)
    {
        var registry = context.ApplicationServices.GetService<IServiceProviderIsService>();
        if (registry is null) return next;

        var targets = new List<Target>();
        var parameters = context.MethodInfo.GetParameters();
        for (var index = 0; index < parameters.Length; index++)
        {
            var type = parameters[index].ParameterType;
            if (!IsCandidate(type)) continue;

            if (registry.IsService(ValidatorType(type)))
                targets.Add(new Target(index, null, ValidatorType(type), Prefix: null));

            // A contract bound as parameters may carry its JSON body in a [FromBody] member.
            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!IsCandidate(property.PropertyType)) continue;
                if (!property.GetCustomAttributes(inherit: true).OfType<IFromBodyMetadata>().Any()) continue;
                if (registry.IsService(ValidatorType(property.PropertyType)))
                    targets.Add(new Target(index, property, ValidatorType(property.PropertyType), Prefix: property.Name + "."));
            }
        }

        if (targets.Count == 0) return next;

        return async invocation =>
        {
            Dictionary<string, List<string>>? errors = null;
            var services = invocation.HttpContext.RequestServices;
            var cancellation = invocation.HttpContext.RequestAborted;

            foreach (var target in targets)
            {
                var argument = invocation.Arguments[target.Index];
                var instance = target.Property is null ? argument : argument is null ? null : target.Property.GetValue(argument);
                if (instance is null) continue;

                foreach (var validator in services.GetServices(target.ValidatorType).OfType<IValidator>())
                {
                    var result = await validator.ValidateAsync(new ValidationContext<object>(instance), cancellation);
                    foreach (var failure in result.Errors)
                    {
                        errors ??= new Dictionary<string, List<string>>(StringComparer.Ordinal);
                        var key = target.Prefix + failure.PropertyName;
                        if (!errors.TryGetValue(key, out var messages)) errors[key] = messages = [];
                        messages.Add(failure.ErrorMessage);
                    }
                }
            }

            if (errors is not null)
                return TypedResults.ValidationProblem(errors.ToDictionary(e => e.Key, e => e.Value.ToArray(), StringComparer.Ordinal));

            return await next(invocation);
        };
    }

    private static Type ValidatorType(Type type) => typeof(IValidator<>).MakeGenericType(type);

    private static bool IsCandidate(Type type) =>
        !type.IsPrimitive && type != typeof(string) && type != typeof(CancellationToken) && type != typeof(HttpContext)
        && !type.IsGenericTypeDefinition && !type.ContainsGenericParameters;

    private sealed record Target(int Index, PropertyInfo? Property, Type ValidatorType, string? Prefix);
}
