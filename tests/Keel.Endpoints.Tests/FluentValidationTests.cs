using System.Net;
using System.Net.Http.Json;
using FluentValidation;
using Keel.Endpoints.FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using static Keel.Endpoints.Tests.Harness;

namespace Keel.Endpoints.Tests;

internal static class FluentValidationTests
{
    public static void Register()
    {
        Test("FluentValidation: an invalid contract returns 400 with errors per property", async () =>
        {
            await using var host = await FvHost.StartAsync(withFilter: true);
            var response = await host.Http.PostAsJsonAsync("/api/orders", new { customer = "", quantity = 0 });
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

            var body = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
            Assert.True(body!.Errors.ContainsKey("Customer") && body.Errors.ContainsKey("Quantity"));
        });

        Test("FluentValidation: a valid contract reaches the handler", async () =>
        {
            await using var host = await FvHost.StartAsync(withFilter: true);
            var response = await host.Http.PostAsJsonAsync("/api/orders", new { customer = "Acme", quantity = 2 });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("Acme x2", await response.Content.ReadAsStringAsync());
        });

        Test("FluentValidation: async rules run (MustAsync)", async () =>
        {
            await using var host = await FvHost.StartAsync(withFilter: true);
            var response = await host.Http.PostAsJsonAsync("/api/orders", new { customer = "blocked", quantity = 1 });
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.True((await response.Content.ReadAsStringAsync()).Contains("is blocked"));
        });

        Test("FluentValidation: a [FromBody] member of a parameters contract is validated, key prefixed", async () =>
        {
            await using var host = await FvHost.StartAsync(withFilter: true);
            var response = await host.Http.PutAsJsonAsync("/api/orders/7/note", new { text = "" });
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

            var body = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
            Assert.True(body!.Errors.ContainsKey("Body.Text"));

            var ok = await host.Http.PutAsJsonAsync("/api/orders/7/note", new { text = "ship monday" });
            Assert.Equal(HttpStatusCode.NoContent, ok.StatusCode);
        });

        Test("FluentValidation: endpoints without a validator are untouched", async () =>
        {
            await using var host = await FvHost.StartAsync(withFilter: true);
            var response = await host.Http.GetAsync("/api/orders/ping");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        });

        Test("FluentValidation: opt-in, without WithFluentValidation() nothing is validated", async () =>
        {
            await using var host = await FvHost.StartAsync(withFilter: false);
            var response = await host.Http.PostAsJsonAsync("/api/orders", new { customer = "", quantity = 0 });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        });
    }
}

internal sealed class FvHost : IAsyncDisposable
{
    private readonly WebApplication _app;
    public HttpClient Http { get; }

    private FvHost(WebApplication app, HttpClient http) => (_app, Http) = (app, http);

    public static async Task<FvHost> StartAsync(bool withFilter)
    {
        var builder = WebApplication.CreateBuilder(["--urls", "http://127.0.0.1:0", "--Logging:LogLevel:Default=Warning"]);
        builder.Services.AddKeelEndpoints();
        builder.Services.AddProblemDetails();
        builder.Services.AddKeelFluentValidation(typeof(FvHost).Assembly);

        var app = builder.Build();
        var group = app.MapGroup("/api").AllowAnonymous();
        group.MapEndpoint<PlaceOrderEndpoint>();
        group.MapEndpoint<AddNoteEndpoint>();
        group.MapEndpoint<PingEndpoint>();
        if (withFilter) group.WithFluentValidation();

        await app.StartAsync();
        return new FvHost(app, new HttpClient { BaseAddress = new Uri(app.Urls.First()) });
    }

    public async ValueTask DisposeAsync()
    {
        Http.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}

public sealed record PlaceOrder(string Customer, int Quantity);

public sealed class PlaceOrderValidator : AbstractValidator<PlaceOrder>
{
    public PlaceOrderValidator()
    {
        RuleFor(x => x.Customer).NotEmpty();
        RuleFor(x => x.Quantity).GreaterThan(0);
        RuleFor(x => x.Customer)
            .MustAsync(async (customer, ct) => { await Task.Yield(); return customer != "blocked"; })
            .WithMessage("Customer is blocked.");
    }
}

[Post("orders")]
public sealed class PlaceOrderEndpoint : Endpoint<PlaceOrder, string>
{
    public override Task<string> HandleAsync(PlaceOrder request, CancellationToken ct) =>
        Task.FromResult($"{request.Customer} x{request.Quantity}");
}

public sealed record NoteBody(string Text);

public sealed class NoteBodyValidator : AbstractValidator<NoteBody>
{
    public NoteBodyValidator() => RuleFor(x => x.Text).NotEmpty();
}

[BindRequest(RequestBinding.Parameters)]
public sealed record AddNote(int OrderId, [property: FromBody] NoteBody Body);

[Put("orders/{orderId}/note")]
public sealed class AddNoteEndpoint : Endpoint<AddNote>
{
    public override Task HandleAsync(AddNote request, CancellationToken ct) => Task.CompletedTask;
}

[Get("orders/ping")]
[AllowAnonymous]
public sealed class PingEndpoint : EndpointWithoutRequest<string>
{
    public override Task<string> HandleAsync(CancellationToken ct) => Task.FromResult("pong");
}
