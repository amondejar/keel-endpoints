using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;

// Endpoints for VersioningTests: two versions of Orders.Get by folder, a list declared for two versions on the
// class, and a version-neutral health check.

namespace Keel.Endpoints.Tests.VersioningFixtures.V1.Orders.Get
{
    public sealed record GetOrder(string Id);

    public sealed record OrderV1(string Id, decimal Total);

    [Get("orders/{id}"), AllowAnonymous]
    public sealed class Endpoint : Endpoint<GetOrder, OrderV1>
    {
        public override Task<OrderV1> HandleAsync(GetOrder request, CancellationToken ct) => Task.FromResult(new OrderV1(request.Id, 10));
    }
}

namespace Keel.Endpoints.Tests.VersioningFixtures.V2.Orders.Get
{
    public sealed record GetOrder(string Id);

    public sealed record Money(decimal Amount, string Currency);

    public sealed record OrderV2(string Id, Money Total);

    [Get("orders/{id}"), AllowAnonymous]
    public sealed class Endpoint : Endpoint<GetOrder, OrderV2>
    {
        public override Task<OrderV2> HandleAsync(GetOrder request, CancellationToken ct) => Task.FromResult(new OrderV2(request.Id, new Money(10, "EUR")));
    }
}

namespace Keel.Endpoints.Tests.VersioningFixtures.V2.Orders.List
{
    /// <summary>Declared on the class: serves 2.0 and 3.0, whatever its folder says.</summary>
    [Get("orders"), ApiVersion(2.0), ApiVersion(3.0), AllowAnonymous]
    public sealed class Endpoint : EndpointWithoutRequest<string[]>
    {
        public override Task<string[]> HandleAsync(CancellationToken ct) => Task.FromResult<string[]>(["7"]);
    }
}

namespace Keel.Endpoints.Tests.VersioningFixtures.Health
{
    [Get("health"), ApiVersionNeutral, AllowAnonymous]
    public sealed class Endpoint : EndpointWithoutRequest<string>
    {
        public override Task<string> HandleAsync(CancellationToken ct) => Task.FromResult("ok");
    }
}
