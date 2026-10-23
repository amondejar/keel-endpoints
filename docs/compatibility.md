# Compatibility

Keel.Endpoints follows [Semantic Versioning](https://semver.org/). From 1.0:

| Part | Promise within a major version |
| --- | --- |
| Public API (`PublicAPI.txt` of each package) | No line removed or changed; lines may be added |
| Behavior of a mapped endpoint (route, binding, responses, metadata) | Unchanged, except bug fixes called out in the changelog |
| Analyzer IDs (KEEL001...) | Never reused; a rule may get more precise, not noisier |
| `api.manifest.json` format | Read by every later version; a new format gets a new `format` number |
| `dotnet keel` command line | Commands and options are not removed; the JSON report's fields stay |
| Templates | May change at any time: they only affect new code |

## Not covered

- `Keel.Endpoints.Infrastructure` (`GeneratedEndpoints`): called by the code the package generates, which
  always ships with the same version. Hidden from IntelliSense; do not call it.
- Analyzer and code fix classes (`Keel.Endpoints.Analyzers`, `Keel.Endpoints.CodeFixes`).
- Experimental ASP.NET Core APIs Keel builds on (.NET 10 validation, ASP0029): if ASP.NET Core changes
  them, Keel follows in a minor version and says so.

## Before 1.0

`Keel.Endpoints.OpenTelemetry` joined in 1.0.0-preview.3. Its public API follows the same rule: once it has
gone two previews without a change, it ships in 1.0.0 with the other packages.

The road to 1.0 is two previews with no public API change (`1.0.0-preview.1`, `1.0.0-preview.2`), then
1.0.0. A change needed in that window starts the count again. The public API test makes any change visible
in the pull request.

## Supported frameworks

.NET 10. A new .NET version is supported in a minor release; dropping one is a major release.
