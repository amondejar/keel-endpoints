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
    public static EndpointDescriptor Create(Type endpointType)
    {
        if (endpointType.IsAbstract || endpointType.IsGenericTypeDefinition || !typeof(EndpointBase).IsAssignableFrom(endpointType))
            throw new InvalidOperationException($"{endpointType} is not a concrete endpoint class.");

        var route = endpointType.GetCustomAttribute<EndpointRouteAttribute>(inherit: false)
            ?? throw new InvalidOperationException(
                $"Endpoint {endpointType.FullName} declares no route. Add [Get(\"...\")], [Post(\"...\")] or another route attribute.");

        // Configure runs on an instance created without its constructor, so endpoints can keep
        // ordinary constructor injection. Configure must therefore not touch injected state.
        var options = new EndpointOptions();
        ((EndpointBase)RuntimeHelpers.GetUninitializedObject(endpointType)).Configure(options);

        var (shape, args) = Shape(endpointType);
        var binding = options.Binding ?? DefaultBinding(route.Method, args.Length > 0 ? args[0] : null, shape);
        var handlerName = HandlerName(shape, binding, options.StreamFormat);

        if (shape == EndpointShape.Stream && options.StreamFormat == StreamFormat.ServerSentEvents)
        {
            typeof(Handlers.EventTypes<>).MakeGenericType(endpointType)
                .GetField(nameof(Handlers.EventTypes<object>.Value))!
                .SetValue(null, options.EventType);
        }

        var method = typeof(Handlers).GetMethod(handlerName, BindingFlags.Public | BindingFlags.Static)!
            .MakeGenericMethod([endpointType, .. args]);
        var delegateType = Expression.GetDelegateType([.. method.GetParameters().Select(p => p.ParameterType), method.ReturnType]);

        var requestType = shape == EndpointShape.WithoutRequest ? null : args[0];
        return new EndpointDescriptor(endpointType, route, options, method.CreateDelegate(delegateType), requestType);
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
