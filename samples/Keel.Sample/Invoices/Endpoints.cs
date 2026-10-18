using System.Runtime.CompilerServices;
using Keel.Endpoints;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;

// One folder per operation is the intended layout (Invoices/Get/Endpoint.cs, ...).
// The sample keeps them in one file so it reads top to bottom.

namespace Keel.Sample.Invoices.Get
{
    [Get("invoices/{invoiceId}")]
    public sealed class Endpoint(InvoiceStore store) : ResultEndpoint<GetInvoice, Results<Ok<InvoiceView>, NotFound>>
    {
        public override Task<Results<Ok<InvoiceView>, NotFound>> HandleAsync(GetInvoice request, CancellationToken ct) =>
            Task.FromResult<Results<Ok<InvoiceView>, NotFound>>(
                store.Find(request.InvoiceId) is { } invoice ? TypedResults.Ok(invoice) : TypedResults.NotFound());
    }
}

namespace Keel.Sample.Invoices.List
{
    [Get("invoices")]
    public sealed class Endpoint(InvoiceStore store) : Endpoint<ListInvoices, IReadOnlyList<InvoiceView>>
    {
        public override Task<IReadOnlyList<InvoiceView>> HandleAsync(ListInvoices request, CancellationToken ct) =>
            Task.FromResult(store.List(request.Status));
    }
}

namespace Keel.Sample.Invoices.Create
{
    [Post("invoices")]
    public sealed class Endpoint(InvoiceStore store) : ResultEndpoint<CreateInvoice, Created<InvoiceView>>
    {
        public override void Configure(EndpointOptions options) =>
            options.Summary("Creates an invoice").Tags("Invoices");

        public override Task<Created<InvoiceView>> HandleAsync(CreateInvoice request, CancellationToken ct)
        {
            var invoice = store.Add(request.Customer, request.Amount);
            return Task.FromResult(TypedResults.Created($"/api/invoices/{invoice.Id}", invoice));
        }
    }
}

namespace Keel.Sample.Invoices.Rename
{
    [Put("invoices/{invoiceId}/customer")]
    public sealed class Endpoint(InvoiceStore store) : ResultEndpoint<RenameInvoice, Results<NoContent, NotFound>>
    {
        public override Task<Results<NoContent, NotFound>> HandleAsync(RenameInvoice request, CancellationToken ct) =>
            Task.FromResult<Results<NoContent, NotFound>>(
                store.Rename(request.InvoiceId, request.Body.Customer) ? TypedResults.NoContent() : TypedResults.NotFound());
    }
}

namespace Keel.Sample.Invoices.Delete
{
    [Delete("invoices/{invoiceId}")]
    public sealed class Endpoint(InvoiceStore store) : Endpoint<DeleteInvoice>
    {
        public override void Configure(EndpointOptions options) => options.RequireAuthorization("admin");

        public override Task HandleAsync(DeleteInvoice request, CancellationToken ct)
        {
            store.Remove(request.InvoiceId);
            return Task.CompletedTask;
        }
    }
}

namespace Keel.Sample.Invoices.Watch
{
    [Get("invoices/events"), ServerSentEvents("invoice")]
    public sealed class Endpoint : StreamEndpoint<WatchInvoices, InvoiceEvent>
    {
        public override async IAsyncEnumerable<InvoiceEvent> HandleAsync(WatchInvoices request, [EnumeratorCancellation] CancellationToken ct)
        {
            for (var i = 1; i <= request.Count; i++)
            {
                await Task.Yield();
                yield return new InvoiceEvent($"inv-{i}", "updated");
            }
        }
    }
}

namespace Keel.Sample.Health
{
    [Get("health")]
    [AllowAnonymous]
    public sealed class Endpoint : EndpointWithoutRequest<HealthStatus>
    {
        public override Task<HealthStatus> HandleAsync(CancellationToken ct) => Task.FromResult(new HealthStatus("ok"));
    }

    public sealed record HealthStatus(string Status);
}
