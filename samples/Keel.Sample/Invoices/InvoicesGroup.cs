using Keel.Endpoints;

namespace Keel.Sample.Invoices;

/// <summary>
/// Configuration for every endpoint under <c>Keel.Sample.Invoices</c>, declared once. Groups run before
/// each endpoint's own <c>Configure</c>; here, a filter that labels the responses of the feature.
/// </summary>
public sealed class InvoicesGroup : EndpointGroup
{
    public override void Configure(EndpointOptions options) =>
        options.Convention(b => b.AddEndpointFilter(async (context, next) =>
        {
            context.HttpContext.Response.Headers["X-Feature"] = "invoices";
            return await next(context);
        }));
}
