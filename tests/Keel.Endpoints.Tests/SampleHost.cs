using Keel.Endpoints.Testing;
using Keel.Sample;

namespace Keel.Endpoints.Tests;

/// <summary>Runs the sample application on a free local port and hands out typed clients.</summary>
internal sealed class SampleHost : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly Uri _baseAddress;

    private SampleHost(WebApplication app, Uri baseAddress) => (_app, _baseAddress) = (app, baseAddress);

    public static async Task<SampleHost> StartAsync()
    {
        var app = SampleApp.Create(["--urls", "http://127.0.0.1:0", "--Logging:LogLevel:Default=Warning"]);
        await app.StartAsync();
        var address = app.Urls.First();
        return new SampleHost(app, new Uri(address));
    }

    public EndpointClient Client(string? apiKey = "demo")
    {
        var http = new HttpClient { BaseAddress = _baseAddress };
        if (apiKey is not null) http.DefaultRequestHeaders.Add("X-Api-Key", apiKey);
        return new EndpointClient(http, prefix: "/api");
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}
