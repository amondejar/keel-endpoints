using Keel.Endpoints;
using Microsoft.AspNetCore.Authorization;

namespace KeelApi.Features.Health;

public sealed record HealthResponse(string Status);

/// <summary>GET /api/health. Public, so it says so: Keel refuses to start an endpoint with no authorization decision.</summary>
[Get("health"), AllowAnonymous]
public sealed class Endpoint : EndpointWithoutRequest<HealthResponse>
{
    public override Task<HealthResponse> HandleAsync(CancellationToken ct) => Task.FromResult(new HealthResponse("ok"));
}
