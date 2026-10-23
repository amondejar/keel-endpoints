# Validation

## .NET 10 validation

```csharp
builder.Services.AddValidation();

[ValidatableType]
public sealed record CreateOrder([property: Required, StringLength(100)] string Customer, [property: Range(1, 100)] int Quantity);
```

DataAnnotations run before the handler; a failure answers `400` with validation problem details, and the
handler never runs. Nested types, collections and `IValidatableObject` work as in any .NET 10 API.

**Why `[ValidatableType]`.** The .NET 10 validation generator finds contracts by reading `Map*` calls in
source. Endpoint classes are mapped by Keel, so it cannot see them: mark each contract that has rules
(KEEL004 reports a missing one and fixes it). A contract that only wraps a validatable member (the
`[FromBody]` body of a parameters-bound contract) needs the attribute too. Under [Native AOT](native-aot.md)
the registration is ordinary source and the attribute is not needed.

**Endpoints with nothing to validate** are excluded from the validation filter (`DisableValidation()`), so
they do not pay for it on every request.

Call `AddValidation()` once per project: the .NET 10 validation generator fails the build (CS8785, "hintName
must be unique") when a project has two call sites.

## FluentValidation

```bash
dotnet add package Keel.Endpoints.FluentValidation --prerelease
```

```csharp
builder.Services.AddKeelFluentValidation(typeof(Program).Assembly);
app.MapKeelEndpoints("/api").RequireAuthorization().WithFluentValidation();
```

Each argument with a registered `IValidator<T>` is validated before the handler, including the
`[FromBody]` member of a parameters-bound contract, with the same `400` response. Both kinds of validation
can run side by side.

## The 400 in OpenAPI

Endpoints whose contract has rules, through either kind of validation, document the `400` validation
problem response.
