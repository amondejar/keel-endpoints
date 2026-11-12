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

namespace Keel.Endpoints.Tests.McpFixtures.Broken.Twice
{
    [Get("twice"), AllowAnonymous]
    public sealed class Endpoint : EndpointWithoutRequest<string>
    {
        public override void Configure(EndpointOptions options) =>
            options.ExposeAsTool("Once.").ExposeAsTool("Twice.");

        public override Task<string> HandleAsync(CancellationToken ct) => Task.FromResult("x");
    }
}

namespace Keel.Endpoints.Tests.McpFixtures.Grouped
{
    // A group must not expose its endpoints as tools.
    public sealed class GroupedGroup : EndpointGroup
    {
        public override void Configure(EndpointOptions options) => options.AllowAnonymous().ExposeAsTool("Everything.");
    }

    [Get("grouped")]
    public sealed class Endpoint : EndpointWithoutRequest<string>
    {
        public override Task<string> HandleAsync(CancellationToken ct) => Task.FromResult("x");
    }
}

// Lean results: a list with nullable members and non-ASCII text, where compaction, caps, field selection and the
// table format show.
namespace Keel.Endpoints.Tests.McpFixtures.Members
{
    public sealed record MemberView(int Id, string Name, string City, string? Email, string? Note);

    public static class MemberData
    {
        public static readonly MemberView[] All =
        [
            .. Enumerable.Range(1, 12).Select(i => new MemberView(
                i,
                i % 2 == 0 ? $"María Núñez {i}" : $"Jordi Puig {i}",
                i % 3 == 0 ? "Málaga" : "Barcelona",
                i % 4 == 0 ? $"m{i}@example.com" : null,
                i == 5 ? "VIP, \"gold\"" : null)),
        ];
    }
}

namespace Keel.Endpoints.Tests.McpFixtures.Members.List
{
    public sealed record ListMembers(string? City);

    [Get("members"), AllowAnonymous]
    public sealed class Endpoint : Endpoint<ListMembers, MemberView[]>
    {
        public override void Configure(EndpointOptions options) => options.ExposeAsTool("Lists the members, optionally of one city.");

        public override Task<MemberView[]> HandleAsync(ListMembers request, CancellationToken ct) =>
            Task.FromResult(MemberData.All.Where(m => request.City is null || m.City == request.City).ToArray());
    }
}

namespace Keel.Endpoints.Tests.McpFixtures.Members.Table
{
    public sealed record ListMemberRows(string? City);

    // Every result option on the tool itself.
    [Get("members/table"), AllowAnonymous]
    public sealed class Endpoint : Endpoint<ListMemberRows, MemberView[]>
    {
        public override void Configure(EndpointOptions options) => options.ExposeAsTool("Lists the members as a table.", tool =>
        {
            tool.Format = Keel.Endpoints.Mcp.McpResultFormat.Table;
            tool.MaxItems = 5;
            tool.FieldSelection = true;
        });

        public override Task<MemberView[]> HandleAsync(ListMemberRows request, CancellationToken ct) =>
            Task.FromResult(MemberData.All.Where(m => request.City is null || m.City == request.City).ToArray());
    }
}

namespace Keel.Endpoints.Tests.McpFixtures.Members.Get
{
    public sealed record GetMember(int Id);

    [Get("members/{id}"), AllowAnonymous]
    public sealed class Endpoint : Endpoint<GetMember, MemberView>
    {
        public override void Configure(EndpointOptions options) => options.ExposeAsTool("Gets a member.", tool => tool.FieldSelection = true);

        public override Task<MemberView> HandleAsync(GetMember request, CancellationToken ct) =>
            Task.FromResult(MemberData.All.Single(m => m.Id == request.Id));
    }
}

namespace Keel.Endpoints.Tests.McpFixtures.Members.BadCap
{
    public sealed record GetFirstMember(int Id);

    // MaxItems on a result that is not a list: a mistake the host reports at startup.
    [Get("members/first/{id}"), AllowAnonymous]
    public sealed class Endpoint : Endpoint<GetFirstMember, MemberView>
    {
        public override void Configure(EndpointOptions options) => options.ExposeAsTool("Gets the first member.", tool => tool.MaxItems = 5);

        public override Task<MemberView> HandleAsync(GetFirstMember request, CancellationToken ct) =>
            Task.FromResult(MemberData.All[0]);
    }
}
