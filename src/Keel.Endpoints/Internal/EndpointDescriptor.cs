using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Keel.Endpoints.Internal;

/// <summary>Everything needed to map one endpoint class, worked out once by reflection.</summary>
internal sealed record EndpointDescriptor(
    Type EndpointType,
    EndpointRouteAttribute Route,
    EndpointOptions Options,
    Delegate Handler,
    Type? RequestType)
{
    internal const string ReflectionMessage =
        "Discovers and binds endpoint classes through reflection. For trimming and Native AOT, call the generated " +
        "app.MapKeelEndpoints(prefix) instead, which registers every endpoint explicitly at compile time.";

    [RequiresUnreferencedCode(ReflectionMessage)]
    [RequiresDynamicCode(ReflectionMessage)]
    public static EndpointDescriptor Create(Type endpointType)
    {
        if (endpointType.IsAbstract || endpointType.IsGenericTypeDefinition || !typeof(EndpointBase).IsAssignableFrom(endpointType))
            throw new InvalidOperationException($"{endpointType} is not a concrete endpoint class.");

        var route = RouteOf(endpointType);
        var options = ConfigureOptions(endpointType);

        var (shape, args) = Shape(endpointType);
        var binding = options.Binding ?? DefaultBinding(route.Method, args.Length > 0 ? args[0] : null, shape);
        var (format, eventType) = Stream(endpointType, options);
        var handlerName = HandlerName(shape, binding, format);

        if (shape == EndpointShape.Stream && format == StreamFormat.ServerSentEvents)
        {
            typeof(EventTypes<>).MakeGenericType(endpointType)
                .GetField(nameof(EventTypes<object>.Value))!
                .SetValue(null, eventType);
        }

        var method = typeof(Handlers).GetMethod(handlerName, BindingFlags.Public | BindingFlags.Static)!
            .MakeGenericMethod([endpointType, .. args]);
        var delegateType = Expression.GetDelegateType([.. method.GetParameters().Select(p => p.ParameterType), method.ReturnType]);

        var requestType = shape == EndpointShape.WithoutRequest ? null : args[0];
        return new EndpointDescriptor(endpointType, route, options, method.CreateDelegate(delegateType), requestType);
    }

    public static EndpointRouteAttribute RouteOf(Type endpointType) =>
        endpointType.GetCustomAttribute<EndpointRouteAttribute>(inherit: false)
        ?? throw new InvalidOperationException(
            $"Endpoint {endpointType.FullName} declares no route. Add [Get(\"...\")], [Post(\"...\")] or another route attribute.");

    /// <summary>
    /// Runs <see cref="EndpointBase.Configure"/> on an instance created without its constructor, so
    /// endpoints keep ordinary constructor injection. Configure must therefore not touch injected state.
    /// </summary>
    public static EndpointOptions ConfigureOptions(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.NonPublicConstructors)]
        Type endpointType)
    {
        var options = new EndpointOptions();
        ((EndpointBase)RuntimeHelpers.GetUninitializedObject(endpointType)).Configure(options);
        return options;
    }

    /// <summary>The stream format: <see cref="ServerSentEventsAttribute"/> or <c>options.StreamFormat</c>.</summary>
    public static (StreamFormat Format, string? EventType) Stream(Type endpointType, EndpointOptions options)
    {
        var attribute = endpointType.GetCustomAttribute<ServerSentEventsAttribute>(inherit: false);
        return attribute is not null
            ? (StreamFormat.ServerSentEvents, options.EventType ?? attribute.EventType)
            : (options.StreamFormat, options.EventType);
    }

    private static (EndpointShape, Type[]) Shape(Type endpointType)
    {
        for (var t = endpointType.BaseType; t is not null; t = t.BaseType)
        {
            if (!t.IsGenericType) continue;
            var def = t.GetGenericTypeDefinition();
            var args = t.GetGenericArguments();
            if (def == typeof(Endpoint<,>)) return (EndpointShape.Response, args);
            if (def == typeof(Endpoint<>)) return (EndpointShape.NoContent, args);
            if (def == typeof(EndpointWithoutRequest<>)) return (EndpointShape.WithoutRequest, args);
            if (def == typeof(ResultEndpoint<,>)) return (EndpointShape.Result, args);
            if (def == typeof(StreamEndpoint<,>)) return (EndpointShape.Stream, args);
        }

        throw new InvalidOperationException(
            $"{endpointType.FullName} must derive from Endpoint<,>, Endpoint<>, EndpointWithoutRequest<>, ResultEndpoint<,> or StreamEndpoint<,>.");
    }

    private static RequestBinding DefaultBinding(string method, Type? requestType, EndpointShape shape)
    {
        if (shape == EndpointShape.WithoutRequest) return RequestBinding.Parameters;
        if (requestType?.GetCustomAttribute<BindRequestAttribute>() is { } explicitBinding) return explicitBinding.Binding;
        return HttpMethods.IsPost(method) || HttpMethods.IsPut(method) || HttpMethods.IsPatch(method)
            ? RequestBinding.Body
            : RequestBinding.Parameters;
    }

    private static string HandlerName(EndpointShape shape, RequestBinding binding, StreamFormat format)
    {
        var from = binding == RequestBinding.Body ? "FromBody" : "FromParameters";
        return shape switch
        {
            EndpointShape.Response => nameof(Handlers.ResponseFromParameters).Replace("FromParameters", from),
            EndpointShape.NoContent => nameof(Handlers.NoContentFromParameters).Replace("FromParameters", from),
            EndpointShape.WithoutRequest => nameof(Handlers.WithoutRequest),
            EndpointShape.Result => nameof(Handlers.ResultFromParameters).Replace("FromParameters", from),
            EndpointShape.Stream when format == StreamFormat.ServerSentEvents => nameof(Handlers.EventStreamFromParameters).Replace("FromParameters", from),
            EndpointShape.Stream => nameof(Handlers.JsonStreamFromParameters).Replace("FromParameters", from),
            _ => throw new ArgumentOutOfRangeException(nameof(shape)),
        };
    }

    private enum EndpointShape { Response, NoContent, WithoutRequest, Result, Stream }
}

/// <summary>The SSE event type per endpoint, read by the handler on each request.</summary>
internal static class EventTypes<TEndpoint>
{
    public static string? Value;
}
