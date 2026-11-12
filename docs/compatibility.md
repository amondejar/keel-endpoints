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

## How it is checked

- **`PublicAPI.txt`**: a test fails on any change to a package's public API until the file is updated, so every
  change is visible in the pull request. From 1.0, a removed or changed line needs a major version.
- **Package validation**: `dotnet pack` compares each library package with the last published version and
  fails on a binary breaking change (`Directory.Build.targets`, `PackageValidationBaselineVersion`).
- **`api.manifest.json`** of the sample: the HTTP behavior Keel produces for a set of endpoints, compared in a
  test, so a change in routing, binding or metadata shows up as a manifest diff.

## The road to 1.0

The release candidates fix what showed up building full applications and agents on Keel. Since `1.0.0-rc.1` the
public API has only grown (approvals and idempotent retries for agents in rc.6 and rc.7, lean tool results in rc.8):
no line of a `PublicAPI.txt` has been removed or changed. 1.0.0 will be the last release candidate as it is, once
one has been used in real applications without needing a change to the public API; a fix that changes it needs
another release candidate.

## Supported frameworks

.NET 10. A new .NET version is supported in a minor release; dropping one is a major release.
