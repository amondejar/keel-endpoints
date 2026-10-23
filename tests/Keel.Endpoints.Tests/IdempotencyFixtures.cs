using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Validation;

// Endpoints for IdempotencyTests, in a feature namespace of their own (Payments).

namespace Keel.Endpoints.Tests.IdempotencyFixtures.Payments
{
    public sealed record Payment(int Number, string Account, decimal Amount);

    /// <summary>Counts handler runs, and can hold a request open or make it fail.</summary>
    public sealed class Ledger
    {
        private int _runs;

        public int Runs => _runs;

        public TaskCompletionSource? Gate { get; set; }

        public TaskCompletionSource Entered { get; set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool FailNext { get; set; }

        public async Task<int> RunAsync()
        {
            var number = Interlocked.Increment(ref _runs);
            Entered.TrySetResult();
            if (Gate is { } gate) await gate.Task;
            if (FailNext)
            {
                FailNext = false;
                throw new InvalidOperationException("payment provider down");
            }

            return number;
        }
    }
}

namespace Keel.Endpoints.Tests.IdempotencyFixtures.Payments.Create
{
    [ValidatableType]
    public sealed record CreatePayment([property: Required] string Account, [property: Range(1, 1000)] decimal Amount);

    [Post("payments"), AllowAnonymous]
    public sealed class Endpoint(Ledger ledger) : ResultEndpoint<CreatePayment, Created<Payment>>
    {
        public override void Configure(EndpointOptions options) => options.Idempotent();

        public override async Task<Created<Payment>> HandleAsync(CreatePayment request, CancellationToken ct)
        {
            var number = await ledger.RunAsync();
            return TypedResults.Created($"/payments/{number}", new Payment(number, request.Account, request.Amount));
        }
    }
}

namespace Keel.Endpoints.Tests.IdempotencyFixtures.Payments.Capture
{
    [BindRequest(RequestBinding.Parameters)]
    public sealed record CapturePayment(string Id);

    [Patch("payments/{id}"), AllowAnonymous]
    public sealed class Endpoint(Ledger ledger) : Endpoint<CapturePayment, Payment>
    {
        public override void Configure(EndpointOptions options) => options.Idempotent(o => o.RequireKey = true);

        public override async Task<Payment> HandleAsync(CapturePayment request, CancellationToken ct) =>
            new(await ledger.RunAsync(), request.Id, 0);
    }
}

namespace Keel.Endpoints.Tests.IdempotencyFixtures.Refunds.Create
{
    public sealed record CreateRefund(string Account, decimal Amount);

    /// <summary>Made idempotent by the route group, and returns a plain object rather than an IResult.</summary>
    [Post("refunds"), AllowAnonymous]
    public sealed class Endpoint(Payments.Ledger ledger) : Endpoint<CreateRefund, Payments.Payment>
    {
        public override async Task<Payments.Payment> HandleAsync(CreateRefund request, CancellationToken ct) =>
            new(await ledger.RunAsync(), request.Account, -request.Amount);
    }
}

namespace Keel.Endpoints.Tests.IdempotencyFixtures.Refunds.Get
{
    public sealed record GetRefund(string Id);

    [Get("refunds/{id}"), AllowAnonymous]
    public sealed class Endpoint(Payments.Ledger ledger) : Endpoint<GetRefund, Payments.Payment>
    {
        public override async Task<Payments.Payment> HandleAsync(GetRefund request, CancellationToken ct) =>
            new(await ledger.RunAsync(), request.Id, 0);
    }
}
