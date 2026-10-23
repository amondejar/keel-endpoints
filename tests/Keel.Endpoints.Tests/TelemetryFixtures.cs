using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Validation;

// Endpoints for TelemetryTests, in a feature namespace of their own (Orders).

namespace Keel.Endpoints.Tests.TelemetryFixtures.Orders
{
    public sealed record Order(string Id, string Name);

    public sealed class OrderBook
    {
        public Order Find(string id) => id == "boom" ? throw new InvalidOperationException("boom") : new Order(id, "anvil");
    }
}

namespace Keel.Endpoints.Tests.TelemetryFixtures.Orders.Get
{
    public sealed record GetOrder(string Id);

    [Get("orders/{id}"), AllowAnonymous]
    public sealed class Endpoint(OrderBook book, ILogger<Endpoint> logger) : Endpoint<GetOrder, Order>
    {
        public override Task<Order> HandleAsync(GetOrder request, CancellationToken ct)
        {
            logger.LogInformation("Reading order {Id}", request.Id);
            return Task.FromResult(book.Find(request.Id));
        }
    }
}

namespace Keel.Endpoints.Tests.TelemetryFixtures.Orders.Create
{
    [ValidatableType]
    public sealed record CreateOrder([property: Required] string Name, [property: Range(1, 10)] int Quantity);

    [Post("orders"), AllowAnonymous]
    public sealed class Endpoint(OrderBook book) : Endpoint<CreateOrder, Order>
    {
        public override Task<Order> HandleAsync(CreateOrder request, CancellationToken ct) => Task.FromResult(book.Find(request.Name));
    }
}
