using System.Collections.Concurrent;

namespace Keel.Sample.Invoices;

/// <summary>An in-memory store, standing in for whatever the application really uses.</summary>
public sealed class InvoiceStore
{
    private readonly ConcurrentDictionary<string, InvoiceView> _invoices = new();
    private int _next;

    public InvoiceView Add(string customer, decimal amount)
    {
        var id = $"inv-{Interlocked.Increment(ref _next)}";
        var invoice = new InvoiceView(id, customer, amount, "open");
        _invoices[id] = invoice;
        return invoice;
    }

    public InvoiceView? Find(string id) => _invoices.GetValueOrDefault(id);

    public IReadOnlyList<InvoiceView> List(string? status) =>
        [.. _invoices.Values.Where(i => status is null || i.Status == status).OrderBy(i => i.Id, StringComparer.Ordinal)];

    public bool Rename(string id, string customer)
    {
        if (!_invoices.TryGetValue(id, out var invoice)) return false;
        _invoices[id] = invoice with { Customer = customer };
        return true;
    }

    public bool Remove(string id) => _invoices.TryRemove(id, out _);
}
