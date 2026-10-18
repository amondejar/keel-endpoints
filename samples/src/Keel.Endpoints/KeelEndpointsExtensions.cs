using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Keel.Endpoints.Internal;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Keel.Endpoints;

/// <summary>Registration and mapping entry points.</summary>
public static class KeelEndpointsExtensions
{
    /// <summary>
    /// Registers Keel.Endpoints. Call <c>AddValidation()</c> as well to turn on .NET 10's
    /// native validation for request contracts.
    /// </summary>
    public static IServiceCollection AddKeelEndpoints(this IServiceCollection services, Action<KeelOptions>? configure = null)
    {
        services.AddOptions<KeelOptions>();
        if (configure is not null) services.Configure(configure);

        // Builds the endpoint table at startup, so a configuration error (for example an endpoint
        // with no authorization decision) stops the host instead of surfacing on the first request.
        services.AddHostedService<EndpointTableVerifier>();
        return services;
    }

    /// <summary>
    /// Maps every endpoint class in <paramref name="assembly"/> under <paramref name="prefix"/>, found by
    /// reflection. Returns the route group, so group-wide conventions stay plain ASP.NET Core:
    /// <c>app.MapKeelEndpoints(typeof(Program).Assembly, "/api").RequireAuthorization();</c>
    /// For trimming and Native AOT use the generated <c>app.MapKeelEndpoints(prefix)</c> instead.
    /// </summary>
    [RequiresUnreferencedCode(EndpointDescriptor.ReflectionMessage)]
    [RequiresDynamicCode(EndpointDescriptor.ReflectionMessage)]
    public static RouteGroupBuilder MapKeelEndpoints(this IEndpointRouteBuilder app, Assembly assembly, string prefix = "")
    {
        ArgumentNullException.ThrowIfNull(assembly);
        var group = app.MapGroup(prefix);

        var endpointTypes = assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false, IsGenericTypeDefinition: false }
                        && typeof(EndpointBase).IsAssignableFrom(t))
            .OrderBy(t => t.FullName, StringComparer.Ordinal);

        foreach (var type in endpointTypes) group.MapEndpoint(type);
        return group;
    }

    /// <summary>Maps one endpoint class explicitly, through reflection.</summary>
    [RequiresUnreferencedCode(EndpointDescriptor.ReflectionMessage)]
    [RequiresDynamicCode(EndpointDescriptor.ReflectionMessage)]
    public static RouteHandlerBuilder MapEndpoint<TEndpoint>(this IEndpointRouteBuilder app)
        where TEndpoint : EndpointBase
        => app.MapEndpoint(typeof(TEndpoint));

    /// <summary>Maps one endpoint class explicitly, through reflection.</summary>
    [RequiresUnreferencedCode(EndpointDescriptor.ReflectionMessage)]
    [RequiresDynamicCode(EndpointDescriptor.ReflectionMessage)]
    public static RouteHandlerBuilder MapEndpoint(this IEndpointRouteBuilder app, Type endpointType)
    {
        var descriptor = EndpointDescriptor.Create(endpointType);
        var builder = app.MapMethods(descriptor.Route.Template, [descriptor.Route.Method], descriptor.Handler);
        return EndpointConventions.Apply(builder, app, endpointType, descriptor.Route, descriptor.Options, descriptor.RequestType);
    }

    /// <summary>
    /// The operation name used for OpenAPI and link generation. A class named <c>Endpoint</c>
    /// takes its name from the last two namespace segments (<c>Invoices.Get</c> becomes
    /// <c>InvoicesGet</c>); any other class uses its own name without an <c>Endpoint</c> suffix.
    /// </summary>
    internal static string OperationName(Type endpointType)
    {
        if (endpointType.Name != "Endpoint")
        {
            return endpointType.Name.EndsWith("Endpoint", StringComparison.Ordinal) && endpointType.Name.Length > "Endpoint".Length
                ? endpointType.Name[..^"Endpoint".Length]
                : endpointType.Name;
        }

        var segments = (endpointType.Namespace ?? string.Empty).Split('.', StringSplitOptions.RemoveEmptyEntries);
        return string.Concat(segments.TakeLast(2));
    }
}

/// <summary>Marks an ASP.NET Core endpoint as produced by Keel.Endpoints.</summary>
public sealed record KeelEndpointMetadata(Type EndpointType);

internal sealed class EndpointTableVerifier(EndpointDataSource dataSource) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _ = dataSource.Endpoints; // building the table runs every Finally convention
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
