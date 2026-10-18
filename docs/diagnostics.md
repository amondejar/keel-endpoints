# Diagnostics

The analyzers ship inside the `Keel.Endpoints` package. Each rule can be tuned in `.editorconfig`, for
example `dotnet_diagnostic.KEEL004.severity = error`.

## KEEL001

**Duplicate route.** Two endpoint classes map the same HTTP method and route template. Parameter names,
constraints, letter case and leading or trailing slashes are ignored, so `things/{id}` and
`/Things/{key:int}/` collide. ASP.NET Core would report an ambiguous match at runtime, on the first request.

**Fix:** change one template or method, or merge the endpoints.

## KEEL002

**Route parameter has no matching contract member.** The template declares `{tenantId}`, but the request
contract has no property or constructor parameter named `tenantId` (case-insensitive), so the value is
silently dropped.

**Fix:** add the member to the contract, or remove the parameter from the template. The code fix adds a
`string` member (a constructor parameter on a positional record, an `init` property otherwise).

## KEEL003

**Route parameter is not bound because the contract is read from the body.** `POST`, `PUT` and `PATCH`
read the whole contract from the JSON body, so route values never reach it.

**Fix:** bind the contract as parameters and mark its body member (the code fix adds the
`[BindRequest]` attribute; moving the body fields into a `[FromBody]` member is up to you):

```csharp
[BindRequest(RequestBinding.Parameters), ValidatableType]
public sealed record RenameInvoice(string InvoiceId, [property: FromBody] RenameInvoiceBody Body);
```

## KEEL004

**Contract has validation rules but is not marked `[ValidatableType]`.** The .NET 10 validation generator
discovers types through the handlers it sees in `Map*` calls. Endpoint classes are mapped through a shared
handler, so their contracts must be marked explicitly, including a contract that only wraps a validatable
member, or validation stops at the contract.

**Fix:** add `[ValidatableType]` (namespace `Microsoft.Extensions.Validation`); the code fix does it. The attribute is marked
experimental in .NET 10; suppress `ASP0029` in the project file.

Limitation: the analyzer reads the default binding from the HTTP method and `[BindRequest]`. A binding set
in `Configure` through `options.Binding` is not visible to it.

## KEEL005

**Endpoint class cannot be registered by the generated code.** The generated `MapKeelEndpoints(prefix)`
lives in the same project but outside the endpoint class, so it can only name classes that are public or
internal and not nested inside a private or protected type. The class is left out of the registration.

**Fix:** make the class `public` or `internal`, or move it out of the private type.

## KEEL006

**Endpoint group cannot be created by the generated code.** The generated registration creates each
`EndpointGroup` with `new`, so the group must be public or internal, not nested inside a private or
protected type, and have a public parameterless constructor. The group is left out of the registration,
which can leave its endpoints without the authorization it declares.

**Fix:** give the group a public parameterless constructor and make it reachable. Groups configure
endpoints; they do not take dependencies.
