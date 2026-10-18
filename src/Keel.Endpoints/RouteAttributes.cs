namespace Keel.Endpoints;

/// <summary>
/// Declares the HTTP method and route template of an endpoint class.
/// Use one of the derived attributes: <see cref="GetAttribute"/>, <see cref="PostAttribute"/>,
/// <see cref="PutAttribute"/>, <see cref="PatchAttribute"/> or <see cref="DeleteAttribute"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public abstract class EndpointRouteAttribute(string method, string template) : Attribute
{
    /// <summary>The HTTP method, for example <c>GET</c>.</summary>
    public string Method { get; } = method;

    /// <summary>The route template, relative to the prefix passed to <c>MapKeelEndpoints</c>.</summary>
    public string Template { get; } = template;
}

/// <summary>Maps the endpoint to <c>GET</c> on <paramref name="template"/>.</summary>
public sealed class GetAttribute(string template) : EndpointRouteAttribute(HttpMethods.Get, template);

/// <summary>Maps the endpoint to <c>POST</c> on <paramref name="template"/>.</summary>
public sealed class PostAttribute(string template) : EndpointRouteAttribute(HttpMethods.Post, template);

/// <summary>Maps the endpoint to <c>PUT</c> on <paramref name="template"/>.</summary>
public sealed class PutAttribute(string template) : EndpointRouteAttribute(HttpMethods.Put, template);

/// <summary>Maps the endpoint to <c>PATCH</c> on <paramref name="template"/>.</summary>
public sealed class PatchAttribute(string template) : EndpointRouteAttribute(HttpMethods.Patch, template);

/// <summary>Maps the endpoint to <c>DELETE</c> on <paramref name="template"/>.</summary>
public sealed class DeleteAttribute(string template) : EndpointRouteAttribute(HttpMethods.Delete, template);

/// <summary>
/// Overrides the default binding of a request contract.
/// By default <c>GET</c> and <c>DELETE</c> bind the contract from route and query
/// (<c>[AsParameters]</c>), and <c>POST</c>, <c>PUT</c> and <c>PATCH</c> bind it from the JSON body.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false)]
public sealed class BindRequestAttribute(RequestBinding binding) : Attribute
{
    /// <summary>The binding to use for the contract.</summary>
    public RequestBinding Binding { get; } = binding;
}

/// <summary>How a request contract is bound.</summary>
public enum RequestBinding
{
    /// <summary>Each constructor parameter binds on its own, as with <c>[AsParameters]</c>:
    /// route, query, header, or body when marked <c>[FromBody]</c>.</summary>
    Parameters,

    /// <summary>The whole contract is read from the JSON body.</summary>
    Body,
}
