using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using Keel.Endpoints.Internal;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Keel.Endpoints.Infrastructure;

/// <summary>
/// Called by the code that Keel.Endpoints generates at compile time. Not meant to be called by hand:
/// the generated <c>MapKeelEndpoints(prefix)</c> registers each endpoint with an explicit, typed
/// <c>MapMethods</c> call (which ASP.NET Core's Request Delegate Generator turns into Native AOT-safe
/// binding), then hands the result here to apply the same conventions as the reflective path.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class GeneratedEndpoints
{
    private const DynamicallyAccessedMemberTypes Ctors = DynamicallyAccessedMemberTypes.PublicConstructors;
    private const DynamicallyAccessedMemberTypes AllCtors = Ctors | DynamicallyAccessedMemberTypes.NonPublicConstructors;

    /// <summary>Creates the endpoint for one request, with constructor injection.</summary>
    public static TEndpoint Activate<[DynamicallyAccessedMembers(Ctors)] TEndpoint>(HttpContext context)
        where TEndpoint : EndpointBase
        => Factory<TEndpoint>.Create(context.RequestServices, null);

    /// <summary>The SSE event type of a streaming endpoint, set when it is mapped.</summary>
    public static string? EventType<TEndpoint>() where TEndpoint : EndpointBase => EventTypes<TEndpoint>.Value;

    /// <summary>
    /// Applies Keel's conventions to a generated registration and checks that runtime configuration
    /// agrees with what the generator decided at compile time. <paramref name="groups"/> are the
    /// <see cref="EndpointGroup"/> classes covering the endpoint, outermost first, found at compile time.
    /// </summary>
    public static RouteHandlerBuilder Apply<[DynamicallyAccessedMembers(AllCtors)] TEndpoint>(
        RouteHandlerBuilder builder,
        IEndpointRouteBuilder app,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] Type? requestType,
        RequestBinding? generatedBinding,
        bool generatedServerSentEvents,
        EndpointGroup[] groups)
        where TEndpoint : EndpointBase
    {
        var endpointType = typeof(TEndpoint);
        var route = EndpointDescriptor.RouteOf(endpointType);
        var options = EndpointDescriptor.ConfigureOptions(endpointType);

        if (options.Binding is { } configured && generatedBinding is { } generated && configured != generated)
        {
            throw new InvalidOperationException(
                $"Endpoint {endpointType.FullName} sets options.Binding = {configured} in Configure, but the generated registration " +
                $"binds it as {generated}. Declare the binding on the contract with [BindRequest(RequestBinding.{configured})] instead.");
        }

        var (format, eventType) = EndpointDescriptor.Stream(endpointType, options);
        if (IsStream(endpointType) && (format == StreamFormat.ServerSentEvents) != generatedServerSentEvents)
        {
            throw new InvalidOperationException(
                $"Endpoint {endpointType.FullName} chooses its stream format in Configure, which the generated registration cannot see. " +
                "Mark the class [ServerSentEvents] instead of setting options.StreamFormat.");
        }

        EventTypes<TEndpoint>.Value = eventType;
        return EndpointConventions.Apply(builder, app, endpointType, route, options, requestType, groups);
    }

    private static bool IsStream(Type endpointType)
    {
        for (var t = endpointType.BaseType; t is not null; t = t.BaseType)
        {
            if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(StreamEndpoint<,>)) return true;
        }

        return false;
    }

    private static class Factory<[DynamicallyAccessedMembers(Ctors)] TEndpoint> where TEndpoint : EndpointBase
    {
        public static readonly ObjectFactory<TEndpoint> Create = ActivatorUtilities.CreateFactory<TEndpoint>([]);
    }
}
