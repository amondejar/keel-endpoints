#if KEEL_OPENAPI
using Microsoft.OpenApi;
using Scalar.AspNetCore;
#endif

namespace Keel.Sample;

/// <summary>
/// The OpenAPI document (<c>/openapi/v1.json</c>) and the Scalar API reference (<c>/scalar</c>),
/// in Development only. The document declares the sample's <c>X-Api-Key</c> header, so Scalar's
/// authentication panel can send <c>demo</c> or <c>admin</c> with every request.
/// </summary>
/// <remarks>
/// Compiled only when the OpenAPI packages are available (normal builds), not in offline builds.
/// </remarks>
public static class ApiReference
{
    public static void AddApiReference(this WebApplicationBuilder builder)
    {
#if KEEL_OPENAPI
        builder.Services.AddOpenApi(options => options.AddDocumentTransformer((document, context, cancellationToken) =>
        {
            document.Components ??= new OpenApiComponents();
            document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
            document.Components.SecuritySchemes[ApiKeyHandler.SchemeName] = new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.ApiKey,
                In = ParameterLocation.Header,
                Name = "X-Api-Key",
                Description = "Use 'demo' for a regular user or 'admin' for an administrator.",
            };

            document.Security ??= [];
            document.Security.Add(new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference(ApiKeyHandler.SchemeName, document)] = [],
            });
            return Task.CompletedTask;
        }));
#endif
    }

    public static void MapApiReference(this WebApplication app)
    {
#if KEEL_OPENAPI
        if (!app.Environment.IsDevelopment()) return;
        app.MapOpenApi();
        app.MapScalarApiReference();
#endif
    }
}
