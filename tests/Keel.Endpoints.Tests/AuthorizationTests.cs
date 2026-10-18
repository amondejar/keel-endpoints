using static Keel.Endpoints.Tests.Harness;

namespace Keel.Endpoints.Tests;

internal static class AuthorizationTests
{
    public static void Register()
    {
        Test("Fail-closed: an endpoint with no authorization decision stops the host at startup", async () =>
        {
            await using var app = Build(configure: null);
            app.MapEndpoint<UndecidedEndpoint>();

            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => app.StartAsync());
            Assert.True(error.Message.Contains(nameof(UndecidedEndpoint)) && error.Message.Contains("no authorization decision"));
        });

        Test("Fail-closed: a decision on the route group satisfies the check", async () =>
        {
            await using var app = Build(configure: null);
            app.MapGroup("").RequireAuthorization().MapEndpoint<UndecidedEndpoint>();
            await app.StartAsync();
            await app.StopAsync();
        });

        Test("Fail-closed: the check can be switched off explicitly", async () =>
        {
            await using var app = Build(configure: o => o.RequireExplicitAuthorization = false);
            app.MapEndpoint<UndecidedEndpoint>();
            await app.StartAsync();
            await app.StopAsync();
        });
    }

    private static WebApplication Build(Action<KeelOptions>? configure)
    {
        var builder = WebApplication.CreateBuilder(["--urls", "http://127.0.0.1:0", "--Logging:LogLevel:Default=None"]);
        builder.Services.AddKeelEndpoints(configure);
        builder.Services.AddAuthorization();
        return builder.Build();
    }
}

[Get("undecided")]
public sealed class UndecidedEndpoint : EndpointWithoutRequest<string>
{
    public override Task<string> HandleAsync(CancellationToken ct) => Task.FromResult("hi");
}
