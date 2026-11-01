using Microsoft.AspNetCore.Authorization;

namespace Keel.Endpoints;

/// <summary>
/// Per-endpoint configuration, set in <see cref="EndpointBase.Configure"/>.
/// Everything here ends up as ordinary ASP.NET Core endpoint conventions.
/// </summary>
public sealed class EndpointOptions
{
    internal List<Action<RouteHandlerBuilder>> Conventions { get; } = [];

    /// <summary>The group these options configure, or <see langword="null"/> for an endpoint's own options.</summary>
    internal Type? Group { get; init; }

    /// <summary>Overrides the contract's default binding for this endpoint.</summary>
    public RequestBinding? Binding { get; set; }

    /// <summary>How a <see cref="StreamEndpoint{TRequest, TItem}"/> writes its items.</summary>
    public StreamFormat StreamFormat { get; set; } = StreamFormat.JsonArray;

    /// <summary>The event type written for each item when <see cref="StreamFormat"/> is
    /// <see cref="StreamFormat.ServerSentEvents"/>.</summary>
    public string? EventType { get; set; }

    /// <summary>
    /// The escape hatch: any convention ASP.NET Core offers on a <see cref="RouteHandlerBuilder"/>
    /// (rate limiting, output caching, CORS, filters, OpenAPI metadata...).
    /// </summary>
    public EndpointOptions Convention(Action<RouteHandlerBuilder> convention)
    {
        ArgumentNullException.ThrowIfNull(convention);
        Conventions.Add(convention);
        return this;
    }

    /// <summary>Requires an authenticated caller, optionally satisfying the given policies.</summary>
    public EndpointOptions RequireAuthorization(params string[] policies) =>
        Convention(b => b.RequireAuthorization(policies));

    /// <summary>Requires a caller satisfying <paramref name="policy"/>.</summary>
    public EndpointOptions RequireAuthorization(AuthorizationPolicy policy) =>
        Convention(b => b.RequireAuthorization(policy));

    /// <summary>Declares the endpoint public. Required for public endpoints when
    /// <see cref="KeelOptions.RequireExplicitAuthorization"/> is on (the default).</summary>
    public EndpointOptions AllowAnonymous() => Convention(b => b.AllowAnonymous());

    /// <summary>Adds OpenAPI tags.</summary>
    public EndpointOptions Tags(params string[] tags) => Convention(b => b.WithTags(tags));

    /// <summary>Sets the OpenAPI summary.</summary>
    public EndpointOptions Summary(string summary) => Convention(b => b.WithSummary(summary));
}

/// <summary>How a streaming endpoint writes its items.</summary>
public enum StreamFormat
{
    /// <summary>A JSON array, flushed as items are produced.</summary>
    JsonArray,

    /// <summary>Server-Sent Events (<c>text/event-stream</c>), one event per item.</summary>
    ServerSentEvents,
}

/// <summary>Application-wide options, set through <c>AddKeelEndpoints</c>.</summary>
public sealed class KeelOptions
{
    /// <summary>
    /// When <see langword="true"/> (the default), the application refuses to start if any endpoint
    /// carries neither authorization metadata nor <c>AllowAnonymous</c>. Metadata applied to a route
    /// group counts, so <c>MapKeelEndpoints().RequireAuthorization()</c> satisfies it for every endpoint.
    /// </summary>
    public bool RequireExplicitAuthorization { get; set; } = true;

    /// <summary>
    /// When <see langword="true"/> (the default), an endpoint with no OpenAPI tag gets one from its
    /// namespace: <c>Billing.Invoices.Get.Endpoint</c> is tagged <c>Invoices</c>, and
    /// <c>Billing.Invoices.GetInvoiceEndpoint</c> is tagged <c>Invoices</c> too. Tags set with
    /// <c>[Tags]</c>, <c>options.Tags(...)</c> or on the route group always win.
    /// </summary>
    public bool TagFromNamespace { get; set; } = true;
}
