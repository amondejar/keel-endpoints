using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Keel.Endpoints.Internal;

/// <summary>
/// Everything Keel adds to an endpoint after it is mapped. Shared by the reflective and the
/// generated registration, so both produce identical endpoints.
/// </summary>
internal static class EndpointConventions
{
    public static RouteHandlerBuilder Apply(
        RouteHandlerBuilder builder,
        IEndpointRouteBuilder app,
        Type endpointType,
        EndpointRouteAttribute route,
        EndpointOptions options,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] Type? requestType)
    {
        builder
            .WithName(KeelEndpointsExtensions.OperationName(endpointType))
            .WithMetadata(new KeelEndpointMetadata(endpointType))
            // Attributes on the endpoint class ([Authorize], [AllowAnonymous], [Tags], [EndpointSummary],
            // [ProducesResponseType]...) become endpoint metadata, as they do on an MVC controller.
            .WithMetadata(endpointType.GetCustomAttributes(inherit: true)
                .Where(a => a is not EndpointRouteAttribute)
                .ToArray());

        foreach (var convention in options.Conventions) convention(builder);

        var keelOptions = app.ServiceProvider.GetRequiredService<IOptions<KeelOptions>>().Value;

        // OpenAPI: document the 400 that .NET 10 validation can return for this contract.
        if (OpenApiConventions.UsesNativeValidation(app.ServiceProvider, requestType))
            builder.ProducesValidationProblem();

        // OpenAPI: tag from the namespace unless a tag was declared anywhere (class, Configure, group).
        if (keelOptions.TagFromNamespace && OpenApiConventions.DefaultTag(endpointType) is { } tag)
        {
            builder.Finally(b =>
            {
                if (!b.Metadata.OfType<ITagsMetadata>().Any()) b.Metadata.Add(new TagsAttribute(tag));
            });
        }

        if (keelOptions.RequireExplicitAuthorization)
        {
            // Finally conventions run after group and endpoint conventions, so authorization
            // declared on the group, the class or in Configure is all visible here.
            builder.Finally(b =>
            {
                var decided = b.Metadata.Any(m => m is IAuthorizeData or IAllowAnonymous);
                if (!decided)
                {
                    throw new InvalidOperationException(
                        $"Endpoint {endpointType.FullName} ({route.Method} {route.Template}) has no authorization decision. " +
                        "Add [Authorize] or [AllowAnonymous] to the class, call options.RequireAuthorization() or options.AllowAnonymous() in Configure, " +
                        "or require authorization on the route group. To turn this check off, set KeelOptions.RequireExplicitAuthorization = false.");
                }
            });
        }

        return builder;
    }
}
