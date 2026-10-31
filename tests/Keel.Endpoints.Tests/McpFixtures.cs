using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Validation;

// Endpoints for McpTests, in a feature namespace of their own (Invoices).

namespace Keel.Endpoints.Tests.McpFixtures.Invoices
{
    public sealed record InvoiceView(int Id, string Customer, decimal Amount);

    public sealed class InvoiceStore
    {
        private readonly Dictionary<int, InvoiceView> _invoices = new()
        {
            [1] = new(1, "Acme", 120m),
            [2] = new(2, "Globex", 75m),
        };

        public IReadOnlyCollection<InvoiceView> All => _invoices.Values;

        public InvoiceView? Find(int id) => _invoices.GetValueOrDefault(id);

        public InvoiceView Add(string customer, decimal amount)
        {
            var invoice = new InvoiceView(_invoices.Keys.Max() + 1, customer, amount);
            _invoices[invoice.Id] = invoice;
            return invoice;
        }

        public bool Remove(int id) => _invoices.Remove(id);
    }
}

namespace Keel.Endpoints.Tests.McpFixtures.Invoices.Get
{
    public sealed record GetInvoice(int Id);

    [Get("invoices/{id}"), AllowAnonymous]
    public sealed class Endpoint(InvoiceStore store) : ResultEndpoint<GetInvoice, Results<Ok<InvoiceView>, NotFound>>
    {
        public override void Configure(EndpointOptions options) => options.ExposeAsTool("Gets an invoice by its number.");

        public override Task<Results<Ok<InvoiceView>, NotFound>> HandleAsync(GetInvoice request, CancellationToken ct) =>
            Task.FromResult<Results<Ok<InvoiceView>, NotFound>>(store.Find(request.Id) is { } invoice ? TypedResults.Ok(invoice) : TypedResults.NotFound());
    }
}

namespace Keel.Endpoints.Tests.McpFixtures.Invoices.Search
{
    public sealed record SearchInvoices(string? Customer, decimal? MinAmount);

    [Get("invoices"), AllowAnonymous]
    public sealed class Endpoint(InvoiceStore store) : Endpoint<SearchInvoices, InvoiceView[]>
    {
        public override void Configure(EndpointOptions options) => options.ExposeAsTool("Lists invoices, optionally of one customer or above an amount.");

        public override Task<InvoiceView[]> HandleAsync(SearchInvoices request, CancellationToken ct) =>
            Task.FromResult(store.All
                .Where(i => request.Customer is null || i.Customer == request.Customer)
                .Where(i => request.MinAmount is null || i.Amount >= request.MinAmount)
                .OrderBy(i => i.Id)
                .ToArray());
    }
}

namespace Keel.Endpoints.Tests.McpFixtures.Invoices.Create
{
    [ValidatableType]
    public sealed record CreateInvoice(
        [property: Required, StringLength(40), Description("Who the invoice is for.")] string Customer,
        [property: Range(1, 10000)] decimal Amount);

    [Post("invoices"), Authorize]
    public sealed class Endpoint(InvoiceStore store) : ResultEndpoint<CreateInvoice, Created<InvoiceView>>
    {
        public override void Configure(EndpointOptions options) =>
            options.ExposeAsTool("Creates an invoice for a customer.", tool => tool.Destructive = false);

        public override Task<Created<InvoiceView>> HandleAsync(CreateInvoice request, CancellationToken ct)
        {
            var invoice = store.Add(request.Customer, request.Amount);
            return Task.FromResult(TypedResults.Created($"/invoices/{invoice.Id}", invoice));
        }
    }
}

namespace Keel.Endpoints.Tests.McpFixtures.Invoices.Delete
{
    public sealed record DeleteInvoice(int Id);

    [Delete("invoices/{id}")]
    public sealed class Endpoint(InvoiceStore store) : Endpoint<DeleteInvoice>
    {
        public override void Configure(EndpointOptions options) =>
            options.RequireAuthorization("admin").ExposeAsTool("Deletes an invoice for good.", tool => tool.Destructive = true);

        public override Task HandleAsync(DeleteInvoice request, CancellationToken ct)
        {
            store.Remove(request.Id);
            return Task.CompletedTask;
        }
    }
}

namespace Keel.Endpoints.Tests.McpFixtures.Invoices.Boom
{
    [Get("invoices/boom"), AllowAnonymous]
    public sealed class Endpoint : EndpointWithoutRequest<InvoiceView>
    {
        public override void Configure(EndpointOptions options) => options.ExposeAsTool("Always fails.", tool => tool.Name = "invoices_boom");

        public override Task<InvoiceView> HandleAsync(CancellationToken ct) =>
            throw new InvalidOperationException("secret connection string in the message");
    }
}

namespace Keel.Endpoints.Tests.McpFixtures.Invoices.Pdf
{
    public sealed record GetPdf(int Id);

    // Not exposed: no ExposeAsTool.
    [Get("invoices/{id}/pdf"), AllowAnonymous]
    public sealed class Endpoint : Endpoint<GetPdf, string>
    {
        public override Task<string> HandleAsync(GetPdf request, CancellationToken ct) => Task.FromResult("%PDF");
    }
}

namespace Keel.Endpoints.Tests.McpFixtures.Broken.Write
{
    public sealed record Archive(int Id);

    // Changes data and does not say whether it is destructive.
    [Post("archive"), AllowAnonymous]
    public sealed class Endpoint : Endpoint<Archive>
    {
        public override void Configure(EndpointOptions options) => options.ExposeAsTool("Archives something.");

        public override Task HandleAsync(Archive request, CancellationToken ct) => Task.CompletedTask;
    }
}

namespace Keel.Endpoints.Tests.McpFixtures.Broken.Same
{
    [Get("same/one"), AllowAnonymous]
    public sealed class One : EndpointWithoutRequest<string>
    {
        public override void Configure(EndpointOptions options) => options.ExposeAsTool("One.", tool => tool.Name = "same");

        public override Task<string> HandleAsync(CancellationToken ct) => Task.FromResult("one");
    }

    [Get("same/two"), AllowAnonymous]
    public sealed class Two : EndpointWithoutRequest<string>
    {
        public override void Configure(EndpointOptions options) => options.ExposeAsTool("Two.", tool => tool.Name = "same");

        public override Task<string> HandleAsync(CancellationToken ct) => Task.FromResult("two");
    }
}
