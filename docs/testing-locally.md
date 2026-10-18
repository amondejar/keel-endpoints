# Trying Keel.Endpoints before publishing

Requires the .NET 10 SDK (`dotnet --version` shows 10.x).

## 1. Build and run the test suite

```bash
dotnet build
dotnet run --project tests/Keel.Endpoints.Tests
```

Expected: `60 passed, 0 failed` (`59` in an offline build, which skips the generated OpenAPI document test). The build itself proves the analyzers load: warnings are errors here.

## 2. Play with the sample API

```bash
dotnet run --project samples/Keel.Sample
```

The browser opens the **Scalar** API reference at `http://localhost:5000/scalar`, with every endpoint
grouped by feature. Open its authentication panel, enter `demo` (or `admin`) as the `X-Api-Key`, and try
the requests from there. The raw OpenAPI document is at `http://localhost:5000/openapi/v1.json`.

You can also open `samples/Keel.Sample/Keel.Sample.http` and send the requests in order, or use curl:

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

## 5. Native AOT

```bash
bash eng/aot-smoke.sh linux-x64        # or win-x64 / osx-arm64; publishes, runs and calls the AOT sample
```

Native AOT needs the platform toolchain: on Windows, Visual Studio's "Desktop development with C++"
workload; on Linux, `clang` and `zlib1g-dev`.

## 6. The dotnet new templates

```bash
bash eng/template-smoke.sh linux-x64   # packs and installs the templates, creates an API and slices, builds, runs and calls it, then the --aot variant
```

By hand, from the repository root:

```bash
dotnet pack src/Keel.Endpoints -c Release -o artifacts
dotnet pack src/Keel.Endpoints.Templates -c Release -o artifacts
dotnet new install artifacts/Keel.Endpoints.Templates.<version>.nupkg
cd ..   # outside the repository
dotnet new keel-api -n Shop && cd Shop
dotnet new keel-slice -n Rename --feature Todos --method put
dotnet run                                 # then open http://localhost:5000/scalar
```

With the `keel-local` source from section 4, the generated project restores the local Keel.Endpoints.
Uninstall with `dotnet new uninstall Keel.Endpoints.Templates`.
