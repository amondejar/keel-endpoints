# Keel.Endpoints.FluentValidation

FluentValidation for [Keel.Endpoints](https://www.nuget.org/packages/Keel.Endpoints) and any ASP.NET Core
Minimal API. An ordinary endpoint filter runs the registered `IValidator<T>` for each argument before the
handler, and failures return `400` validation problem details: the same shape as .NET 10 validation.

```bash
dotnet add package Keel.Endpoints.FluentValidation --prerelease
```

```csharp
builder.Services.AddKeelEndpoints();
builder.Services.AddProblemDetails();
builder.Services.AddKeelFluentValidation(typeof(Program).Assembly); // registers IValidator<T> types

app.MapKeelEndpoints("/api")
   .RequireAuthorization()
   .WithFluentValidation();
```

```csharp
public sealed record CreateInvoice(string Customer, decimal Amount);

public sealed class CreateInvoiceValidator : AbstractValidator<CreateInvoice>
{
    public CreateInvoiceValidator()
    {
        RuleFor(x => x.Customer).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Amount).GreaterThan(0);
    }
}
```

- **Opt-in per group or endpoint.** `WithFluentValidation()` works on a route group, on one endpoint
  (`options.Convention(b => b.WithFluentValidation())`) or on any `MapGet`/`MapPost`.
- **Zero cost where unused.** The filter is only attached to endpoints that have an argument with a
  registered validator.
- **Async rules** (`MustAsync`, `CustomAsync`) run with the request's cancellation token.
- **`[FromBody]` members** of a contract bound as parameters are validated too; their error keys are
  prefixed with the member name (`Body.Customer`).
- **Bring your own registration.** Validators registered with `FluentValidation.DependencyInjectionExtensions`
  or by hand are picked up the same way.

Choose one validation system per contract: if a contract has both DataAnnotations (with `AddValidation()`)
and a FluentValidation validator, both run and both can reject the request.
