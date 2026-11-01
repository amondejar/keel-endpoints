using Keel.Endpoints.Agents;
using Microsoft.AspNetCore.Authorization;

// Endpoints for AgentsTests, in a feature namespace of their own (Payments).

namespace Keel.Endpoints.Tests.AgentsFixtures.Payments
{
    public sealed class PaymentLedger
    {
        private readonly List<string> _actions = [];

        public IReadOnlyList<string> Actions { get { lock (_actions) return [.. _actions]; } }

        public void Record(string action) { lock (_actions) _actions.Add(action); }
    }

    public sealed class RecordingAuditSink : IAgentAuditSink
    {
        private readonly List<AgentAuditEntry> _entries = [];

        public IReadOnlyList<AgentAuditEntry> Entries { get { lock (_entries) return [.. _entries]; } }

        public ValueTask WriteAsync(AgentAuditEntry entry, CancellationToken cancellationToken)
        {
            lock (_entries) _entries.Add(entry);
            return ValueTask.CompletedTask;
        }
    }
}

namespace Keel.Endpoints.Tests.AgentsFixtures.Payments.Refund
{
    public sealed record RefundPayment(int Id, decimal Amount);

    // Not destructive, but money leaves: confirmation asked explicitly, with the call's own data.
    [Post("payments/refund"), AllowAnonymous]
    public sealed class Endpoint(PaymentLedger ledger) : Endpoint<RefundPayment>
    {
        public override void Configure(EndpointOptions options) => options
            .ExposeAsTool("Refunds a payment.", tool => tool.Destructive = false)
            .RequireConfirmation<RefundPayment>(r => $"Refund payment {r.Id}: {r.Amount} EUR");

        public override Task HandleAsync(RefundPayment request, CancellationToken ct)
        {
            ledger.Record($"refund {request.Id} {request.Amount}");
            return Task.CompletedTask;
        }
    }
}

namespace Keel.Endpoints.Tests.AgentsFixtures.Payments.Cancel
{
    public sealed record CancelPayment(int Id);

    // Destructive: confirmed by default.
    [Delete("payments/{id}"), AllowAnonymous]
    public sealed class Endpoint(PaymentLedger ledger) : Endpoint<CancelPayment>
    {
        public override void Configure(EndpointOptions options) =>
            options.ExposeAsTool("Cancels a payment for good.", tool => tool.Destructive = true);

        public override Task HandleAsync(CancelPayment request, CancellationToken ct)
        {
            ledger.Record($"cancel {request.Id}");
            return Task.CompletedTask;
        }
    }
}

namespace Keel.Endpoints.Tests.AgentsFixtures.Payments.Archive
{
    public sealed record ArchivePayment(int Id);

    // Destructive, but easy to undo: opted out.
    [Delete("payments/{id}/archive"), AllowAnonymous]
    public sealed class Endpoint(PaymentLedger ledger) : Endpoint<ArchivePayment>
    {
        public override void Configure(EndpointOptions options) => options
            .ExposeAsTool("Archives a payment.", tool => tool.Destructive = true)
            .WithoutConfirmation();

        public override Task HandleAsync(ArchivePayment request, CancellationToken ct)
        {
            ledger.Record($"archive {request.Id}");
            return Task.CompletedTask;
        }
    }
}

namespace Keel.Endpoints.Tests.AgentsFixtures.Payments.Note
{
    public sealed record AddNote(int Id, string Text);

    // Only adds: no confirmation, but audited.
    [Post("payments/note"), AllowAnonymous]
    public sealed class Endpoint(PaymentLedger ledger) : Endpoint<AddNote>
    {
        public override void Configure(EndpointOptions options) =>
            options.ExposeAsTool("Adds a note to a payment.", tool => tool.Destructive = false);

        public override Task HandleAsync(AddNote request, CancellationToken ct)
        {
            ledger.Record($"note {request.Id}");
            return Task.CompletedTask;
        }
    }
}

namespace Keel.Endpoints.Tests.AgentsFixtures.Payments.List
{
    [Get("payments"), AllowAnonymous]
    public sealed class Endpoint : EndpointWithoutRequest<string[]>
    {
        public override void Configure(EndpointOptions options) => options.ExposeAsTool("Lists payments.");

        public override Task<string[]> HandleAsync(CancellationToken ct) => Task.FromResult<string[]>(["1", "2"]);
    }
}

namespace Keel.Endpoints.Tests.AgentsFixtures.Payments.Payout
{
    public sealed record Payout(string To, decimal Amount);

    // Approved by someone other than the caller (the default).
    [Post("payments/payout"), AllowAnonymous]
    public sealed class Endpoint(PaymentLedger ledger) : Endpoint<Payout>
    {
        public override void Configure(EndpointOptions options) => options
            .ExposeAsTool("Pays money out to a supplier.", tool => tool.Destructive = false)
            .RequireApproval<Payout>(r => $"Pay {r.Amount} EUR to {r.To} <now>");

        public override Task HandleAsync(Payout request, CancellationToken ct)
        {
            ledger.Record($"payout {request.To} {request.Amount}");
            return Task.CompletedTask;
        }
    }
}

namespace Keel.Endpoints.Tests.AgentsFixtures.Payments.Close
{
    public sealed record CloseAccount(int Id);

    // Two supervisors, neither of them the caller.
    [Delete("payments/{id}/account"), AllowAnonymous]
    public sealed class Endpoint(PaymentLedger ledger) : Endpoint<CloseAccount>
    {
        public override void Configure(EndpointOptions options) => options
            .ExposeAsTool("Closes a payment account.", tool => tool.Destructive = true)
            .RequireApproval("Close a payment account", approval =>
            {
                approval.Approvals = 2;
                approval.Policy = "supervisors";
            });

        public override Task HandleAsync(CloseAccount request, CancellationToken ct)
        {
            ledger.Record($"close {request.Id}");
            return Task.CompletedTask;
        }
    }
}
