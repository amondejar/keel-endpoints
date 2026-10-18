using System.Security.Claims;
using System.Text.Encodings.Web;
using Keel.Endpoints;
using Keel.Sample.Invoices;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Keel.Sample;

public static class SampleApp
{
    /// <summary>Builds the sample application. The tests host the same app.</summary>
    public static WebApplication Create(string[] args, Action<WebApplicationBuilder>? configure = null)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.Services.AddKeelEndpoints();
        builder.Services.AddValidation();      // .NET 10 native validation for contracts
        builder.Services.AddProblemDetails();  // RFC 9457 errors, from ASP.NET Core
        builder.Services.AddSingleton<InvoiceStore>();

        builder.Services.AddAuthentication(ApiKeyHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, ApiKeyHandler>(ApiKeyHandler.SchemeName, null);
        builder.Services.AddAuthorizationBuilder()
            .AddPolicy("admin", p => p.RequireRole("admin"));

        builder.AddApiReference();             // OpenAPI document + Scalar UI (Development)

        configure?.Invoke(builder);

        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();

        // Every endpoint requires an authenticated caller unless it says [AllowAnonymous].
        app.MapKeelEndpoints(typeof(SampleApp).Assembly, "/api").RequireAuthorization();
        app.MapApiReference();                 // /openapi/v1.json and /scalar, Development only

        return app;
    }
}

/// <summary>
/// A demo authentication scheme: <c>X-Api-Key: demo</c> is a user, <c>X-Api-Key: admin</c> an admin.
/// Use real authentication in a real application.
/// </summary>
public sealed class ApiKeyHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "ApiKey";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var key = Request.Headers["X-Api-Key"].ToString();
        if (key is not ("demo" or "admin")) return Task.FromResult(AuthenticateResult.NoResult());

        Claim[] claims = key == "admin"
            ? [new(ClaimTypes.Name, key), new(ClaimTypes.Role, "admin")]
            : [new(ClaimTypes.Name, key)];
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
    }
}
