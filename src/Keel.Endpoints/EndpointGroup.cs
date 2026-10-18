namespace Keel.Endpoints;

/// <summary>
/// Configuration shared by every endpoint in a namespace and the namespaces below it: a feature
/// folder's authorization, tags, filters or rate limits, declared once.
/// </summary>
/// <remarks>
/// Groups run outermost namespace first, then the endpoint's own <see cref="EndpointBase.Configure"/>,
/// so the endpoint has the last word. A group's authorization decision counts for
/// <see cref="KeelOptions.RequireExplicitAuthorization"/>. <see cref="EndpointOptions.Binding"/>,
/// <see cref="EndpointOptions.StreamFormat"/> and <see cref="EndpointOptions.EventType"/> belong to each
/// endpoint and cannot be set by a group. A group needs a public parameterless constructor and does not
/// take dependencies.
/// </remarks>
/// <example>
/// <code>
/// namespace Billing.Invoices;
///
/// public sealed class InvoicesGroup : EndpointGroup
/// {
///     public override void Configure(EndpointOptions options) =>
///         options.RequireAuthorization("billing").Tags("Invoices");
/// }
/// </code>
/// </example>
public abstract class EndpointGroup
{
    /// <summary>Configures every endpoint the group covers. Runs once per endpoint at map time.</summary>
    public abstract void Configure(EndpointOptions options);
}
