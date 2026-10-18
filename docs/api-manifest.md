# API manifest

`ApiManifest` (in `Keel.Endpoints.Testing`) turns the HTTP surface of an application into a committed
file, `api.manifest.json`, and fails a test when the application no longer matches it. Breaking changes
are named as such, so they are a decision in a pull request instead of a surprise for clients.

## Setup

```csharp
// xUnit, with Microsoft.AspNetCore.Mvc.Testing
public class ApiManifestTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public void Api_matches_its_manifest() => ApiManifest.Verify(factory.Services);
}
```

- The path (`api.manifest.json` by default) is relative to the test's source file. Pass another one, for
  example `ApiManifest.Verify(factory.Services, "../../src/Shop/api.manifest.json")`.
- The first run creates the file and fails, so it gets reviewed. Commit it.
- Only some routes? `new ApiManifestOptions { Include = d => d.RelativePath?.StartsWith("api/") == true }`.
- Not on xUnit? `Verify` only throws `ApiManifestException`; any test framework works.

## Accepting a change

```bash
KEEL_UPDATE_MANIFEST=1 dotnet test     # rewrites the file if every change is compatible
KEEL_UPDATE_MANIFEST=all dotnet test   # also accepts breaking and security changes
```

PowerShell: `$env:KEEL_UPDATE_MANIFEST = "1"; dotnet test; Remove-Item Env:KEEL_UPDATE_MANIFEST`.

Then commit the file. CI does not set the variable, so a pull request that changes the API without
updating the manifest fails, and one that updates it shows the diff to reviewers.

## What is in the file

```json
"POST api/invoices": {
  "operation": "InvoicesCreate",
  "authorization": "required",
  "body": "CreateInvoice (required)",
  "responses": { "201": "InvoiceView", "400": "validation-problem" }
}
```

Endpoints are keyed by method and route. Values read `type`, `type (required)` and, when not JSON,
`type as media/type`. Types: `string`, `boolean`, `int32`, `int64`, `number`, `date-time`, `date`, `time`,
`duration`, `guid`, `uri`, `binary`, `file`, `any`, `problem`, `validation-problem`, `array<T>`, `map<T>`, or
a name under `types`; a trailing `?` means null is possible. Names, nullability and requiredness come from
the application's JSON options, so they are what goes over the wire. A member is required when it is
JSON-required (`required`, `[JsonRequired]`) or has `[Required]` with no constructor default.

The data comes from ApiExplorer, the same source as the OpenAPI document, so it covers every endpoint
ApiExplorer sees, Keel or not.

## What counts as breaking

Changes are judged from an existing client's side: what it sends may be accepted more widely, never less;
what it reads may gain members, never lose or change them.

| Breaking | Security | Compatible |
| --- | --- | --- |
| Endpoint removed or moved (method or URL shape) | Authorization no longer required | Endpoint added |
| Authorization now required, new policy or role, an authentication scheme dropped | Policy or role no longer required | Route parameter renamed (same URL shape) |
| New required parameter, body or body member | New authentication scheme accepted | New optional parameter or body member |
| Optional parameter or member now required | | Parameter or request member removed (now ignored) |
| Request no longer accepts null, a media type or an enum value | | Request accepts a wider number (`int32` to `int64`) |
| Response member removed, or may now be null | | Response member added, new enum value |
| Response type changed (`int64` to `int32` is fine, the reverse is not) | | New status code documented, non-2xx removed |
| A 2xx status removed, a body removed, JSON switched to another media type | | Operation name changed (generated clients may rename methods) |

`ApiManifest.Compare(baseline, current)` returns the same list as `ApiChange` records, for a custom policy
or for comparing the manifests of two releases.
