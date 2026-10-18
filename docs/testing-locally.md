# Trying Keel.Endpoints before publishing

Requires the .NET 10 SDK (`dotnet --version` shows 10.x).

## 1. Build and run the test suite

```bash
dotnet build
dotnet run --project tests/Keel.Endpoints.Tests
```

Expected: `26 passed, 0 failed`. The build itself proves the analyzers load: warnings are errors here.

## 2. Play with the sample API

```bash
dotnet run --project samples/Keel.Sample
```

Then open `samples/Keel.Sample/Keel.Sample.http` and send the requests in order, or use curl:

```bash
curl http://localhost:5000/api/health
curl -H "X-Api-Key: demo" -H "Content-Type: application/json" \
     -d '{"customer":"","amount":0}' http://localhost:5000/api/invoices
```

`X-Api-Key: demo` is a user; `X-Api-Key: admin` is an admin.

## 3. See the analyzers fire

Add this to `samples/Keel.Sample`, build, and read the errors (KEEL001 duplicate route, KEEL002 orphan
route parameter). Delete the file afterwards.

```csharp
using Keel.Endpoints;
namespace Keel.Sample.Scratch;
public sealed record Q(string Other);
[Microsoft.AspNetCore.Authorization.AllowAnonymous]
[Get("invoices/{id}")]
public sealed class Dup : Endpoint<Q, string>
{
    public override Task<string> HandleAsync(Q r, CancellationToken ct) => Task.FromResult("");
}
```

## 4. Use the package from your own project, without nuget.org

```bash
dotnet pack -c Release -o ./artifacts
```

In your project folder:

```bash
dotnet nuget add source /full/path/to/keel-endpoints/artifacts --name keel-local
dotnet add package Keel.Endpoints --prerelease
```

To pick up a rebuilt package with the same version, clear the cache first:
`dotnet nuget locals global-packages --clear`. Remove the source when done:
`dotnet nuget remove source keel-local`.
