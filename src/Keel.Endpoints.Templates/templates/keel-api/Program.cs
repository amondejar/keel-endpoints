using Keel.Endpoints;
#if (aot)
using KeelApi;
#endif
using KeelApi.Features.Todos;
using Scalar.AspNetCore;

#if (aot)
var builder = WebApplication.CreateSlimBuilder(args);
#else
var builder = WebApplication.CreateBuilder(args);
#endif

builder.Services.AddKeelEndpoints();   // fail-closed: every endpoint must declare who may call it
builder.Services.AddValidation();      // .NET 10 validation for request contracts
builder.Services.AddProblemDetails();  // RFC 9457 error responses
builder.Services.AddOpenApi();
builder.Services.AddSingleton<TodoStore>();
#if (aot)

// Native AOT has no reflection-based JSON: list every type on the wire in AppJsonContext.
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonContext.Default));
#endif

// Add authentication and authorization here, then require it for the whole API:
// app.MapKeelEndpoints("/api").RequireAuthorization();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();               // /openapi/v1.json
    app.MapScalarApiReference();    // /scalar
}

// Generated at compile time: every endpoint class in this project, registered explicitly.
app.MapKeelEndpoints("/api");

app.Run();
