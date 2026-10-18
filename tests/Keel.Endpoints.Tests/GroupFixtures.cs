// Endpoint groups and the endpoints they cover. Each scenario has its own namespace, because a group
// covers its namespace and everything below it, and must not reach the other tests' endpoints.

namespace Keel.Endpoints.Tests.Groups.Shop
{
    /// <summary>Outermost: covers Shop and Shop.Admin.</summary>
    public sealed class ShopGroup : EndpointGroup
    {
        public override void Configure(EndpointOptions options) =>
            options.AllowAnonymous().Convention(b => b.AddEndpointFilter(Order.Append("shop")));
    }

    internal static class Order
    {
        public static Func<EndpointFilterInvocationContext, EndpointFilterDelegate, ValueTask<object?>> Append(string step) =>
            async (context, next) =>
            {
                var headers = context.HttpContext.Response.Headers;
                headers["X-Order"] = headers.TryGetValue("X-Order", out var current) && current.Count > 0 ? $"{current},{step}" : step;
                return await next(context);
            };
    }
}

namespace Keel.Endpoints.Tests.Groups.Shop.Admin
{
    public sealed class AdminGroup : EndpointGroup
    {
        public override void Configure(EndpointOptions options) =>
            options.Tags("Admin").Convention(b => b.AddEndpointFilter(Shop.Order.Append("admin")));
    }

    [Get("shop/admin/ping")]
    public sealed class Endpoint : EndpointWithoutRequest<string>
    {
        public override void Configure(EndpointOptions options) =>
            options.Convention(b => b.AddEndpointFilter(Shop.Order.Append("endpoint")));

        public override Task<string> HandleAsync(CancellationToken ct) => Task.FromResult("pong");
    }
}

namespace Keel.Endpoints.Tests.Groups.Shopping
{
    /// <summary>Not under Shop: "Shopping" only shares a prefix, so ShopGroup does not cover it.</summary>
    [Get("shopping/ping")]
    public sealed class Endpoint : EndpointWithoutRequest<string>
    {
        public override Task<string> HandleAsync(CancellationToken ct) => Task.FromResult("pong");
    }
}

namespace Keel.Endpoints.Tests.Groups.Misconfigured
{
    /// <summary>Binding belongs to endpoints; a group setting it is rejected at map time.</summary>
    public sealed class BindingGroup : EndpointGroup
    {
        public override void Configure(EndpointOptions options)
        {
            options.AllowAnonymous();
            options.Binding = RequestBinding.Body;
        }
    }

    [Get("misconfigured")]
    public sealed class Endpoint : EndpointWithoutRequest<string>
    {
        public override Task<string> HandleAsync(CancellationToken ct) => Task.FromResult("");
    }
}

namespace Keel.Endpoints.Tests.Groups.WithDependencies
{
    /// <summary>Groups are created without DI, so a constructor with parameters is rejected.</summary>
    public sealed class NeedsArgsGroup(string policy) : EndpointGroup
    {
        public override void Configure(EndpointOptions options) => options.RequireAuthorization(policy);
    }

    [Get("with-dependencies")]
    public sealed class Endpoint : EndpointWithoutRequest<string>
    {
        public override Task<string> HandleAsync(CancellationToken ct) => Task.FromResult("");
    }
}
