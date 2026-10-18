# Testing

```bash
dotnet add package Keel.Endpoints.Testing --prerelease
```

## Typed calls

`EndpointClient` builds the request from the endpoint class: method, route, query string and body, so a
test cannot drift from the route it tests.

```csharp
public class OrderTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task Get_returns_the_order()
    {
        var client = new EndpointClient(factory.CreateClient(), prefix: "/api");
        var response = await client.SendAsync<Orders.Get.Endpoint, Order>(new GetOrder("42"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("42", response.Value?.Id);
    }
}
```

`SendAsync<TEndpoint>` returns the raw `HttpResponseMessage`; `SendAsync<TEndpoint, TResponse>` reads the
body. `client.Http` is the underlying `HttpClient`, for headers.

## The API manifest

One test keeps a committed description of the HTTP surface, and fails on any change until it is accepted:

```csharp
[Fact]
public void Api_matches_its_manifest() => ApiManifest.Verify(factory.Services);
```

See [API manifest](api-manifest.md).

## Public API (for library authors)

This repository records each package's public API in `PublicAPI.txt` and fails a test when it changes
(`tests/Keel.Endpoints.Tests/PublicApiTests.cs`); accept with `KEEL_UPDATE_PUBLIC_API=1`. The same file is a
starting point for your own libraries.
