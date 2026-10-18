using System.ComponentModel.DataAnnotations;
using Keel.Endpoints;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Validation;

namespace Keel.Sample.Invoices;

public sealed record InvoiceView(string Id, string Customer, decimal Amount, string Status);

[ValidatableType]
public sealed record CreateInvoice(
    [property: Required, StringLength(100)] string Customer,
    [property: Range(0.01, 1_000_000)] decimal Amount);

public sealed record GetInvoice(string InvoiceId);

public sealed record ListInvoices(string? Status);

public sealed record DeleteInvoice(string InvoiceId);

/// <summary>Route value plus JSON body: bound as parameters, the body member marked [FromBody].
/// Marked [ValidatableType] too, so validation reaches the body member (KEEL004 checks this).</summary>
[BindRequest(RequestBinding.Parameters), ValidatableType]
public sealed record RenameInvoice(string InvoiceId, [property: FromBody] RenameInvoiceBody Body);

[ValidatableType]
public sealed record RenameInvoiceBody([property: Required, StringLength(100)] string Customer);

public sealed record WatchInvoices(int Count = 3);

public sealed record InvoiceEvent(string InvoiceId, string Kind);
