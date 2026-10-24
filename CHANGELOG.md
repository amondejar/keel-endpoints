# Changelog

All notable changes are listed here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/)
and the project uses [Semantic Versioning](https://semver.org/).

## [1.0.0-rc.1] - 2026-10-24

The release candidate for 1.0.0. No public API changes: every package ships the API of `1.0.0-preview.6`.
Fixes for what turned up building a complete application on the previews ([keel-shop](https://github.com/amondejar/keel-shop)).

### Added
- **KEEL007**: an array or collection whose item type has validation rules but no `[ValidatableType]`. The .NET 10
  validation generator does not discover types reached only as collection items, so those items were silently
  not validated. The KEEL004 code fix adds the attribute.
- **Package validation**: `dotnet pack` compares every library package with the last published version
  (`1.0.0-preview.6` for now) and fails on a binary breaking change.
- CI builds and tests on Windows as well as Linux; `.gitattributes` keeps LF line endings on every OS.
- `docs/versioning.md`: one OpenAPI document per version with `Asp.Versioning.OpenApi`
  (`AddApiVersioning().AddOpenApi()`, `MapOpenApi().WithDocumentPerVersion()`, AV0029 and AV0030).

### Changed
- **Server-Sent Events send the response headers at once**, before the first event. Clients and tests used to
  wait, and proxies could time out, until the stream had something to say.
- A single-operation feature directly in `Features` or in a version folder takes its own name as tag and
  operation name: `Features.Health.Endpoint` is `Health` (was tag `Features`, operation `FeaturesHealth`). The
  `keel-api` template's health endpoint is one. Check `api.manifest.json` for an operation rename.
- Templates and samples reference `Microsoft.AspNetCore.OpenApi` 10.0.10, which brings a patched
  `Microsoft.OpenApi` (the explicit 2.7.5 pin is gone) and is what `Asp.Versioning.OpenApi` needs.
- The api-diff action is documented at `@v1`, a tag that follows the latest 1.x release.

## [1.0.0-preview.6] - 2026-10-24

No public API changes in the existing packages.

### Added
- **`Keel.Endpoints.Versioning`** (new, preview): `WithKeelVersioning()` on an Asp.Versioning versioned API
  takes each endpoint's version from its folder (`Features/V2/...`) unless the class or a convention declares
  one, and adds the version to the operation name of endpoints that serve one version (`OrdersGetV2`), which
  two versions of an operation need to route at all.
- `docs/versioning.md`.

### Changed
- KEEL001 no longer reports the same route in two API versions, by `[ApiVersion]`/`[MapToApiVersion]` on the
  class or by a version segment in the namespace.

## [1.0.0-preview.5] - 2026-10-23

No public API changes in the existing packages.

### Added
- **`Keel.Endpoints.Idempotency`** (new, preview): `options.Idempotent()` and `WithIdempotency()` make
  `POST` and `PATCH` safe to retry with an `Idempotency-Key` header. The first response is stored in
  `IDistributedCache` (or an `IIdempotencyStore`) and replayed with `Idempotent-Replayed: true`; a reused key
  with another request gets 422, a retry while the first runs gets 409. Keys are per endpoint and per user.
  AOT-compatible.
- The Native AOT sample makes `POST todos` idempotent, and the AOT smoke test checks a retry.
- `docs/idempotency.md`.

## [1.0.0-preview.4] - 2026-10-23

No public API changes in the library packages.

### Added
- **`Keel.Endpoints.Tool`** (new, preview): `dotnet keel diff <base> [<head>]` compares `api.manifest.json`
  between two git refs or two files and lists breaking, security and compatible changes as text, Markdown or
  JSON; `--fail-on breaking|security|any` sets the exit code.
- **`actions/api-diff`**: a GitHub Action that keeps one pull request comment up to date with the API
  changes, writes them to the job summary and can fail the check. This repository's CI runs it.
- GitHub release notes include the API changes since the previous release.

## [1.0.0-preview.3] - 2026-10-23

No public API changes in the existing packages since 1.0.0-preview.1.

### Added
- **`Keel.Endpoints.OpenTelemetry`** (new package, preview): `AddKeelTelemetry()` tags ASP.NET Core's
  request span and `http.server.request.duration` with `keel.operation` and `keel.feature`, and counts
  validation errors per member in `keel.validation.errors`; `WithHandlerTelemetry()` adds a handler span,
  `keel.handler.duration` and a logging scope; `AddKeelEndpointsInstrumentation()` on the OpenTelemetry
  tracer and meter builders. Built on `System.Diagnostics`, AOT-compatible.
- The Native AOT sample uses it, so CI checks it publishes without warnings.
- `docs/observability.md`.

## [1.0.0-preview.2] - 2026-10-19

No public API changes since 1.0.0-preview.1.

### Added
- Package icon, README badges, `SECURITY.md`, `CODE_OF_CONDUCT.md`, issue and pull request templates.
- Releases create a GitHub release with the version's changelog section and the packages attached.
- CI builds are deterministic (`ContinuousIntegrationBuild`), so the published packages are reproducible.

### Fixed
- The benchmark no longer starts a server, so an IDE launch profile with an https:// URL does not break it.

## [1.0.0-preview.1] - 2026-10-18

The first 1.0 preview: the public API is frozen (see `docs/compatibility.md`).

### Added
- `PublicAPI.txt` for each package, checked by the test suite: any public API change fails until the
  file is updated (`KEEL_UPDATE_PUBLIC_API=1`).
- `benchmarks/Keel.Endpoints.Benchmarks`: Keel endpoints against the same operations as Minimal API
  lambdas, in process; `--check 5` fails when Keel is more than 5% slower. A CI job keeps it running.
- Documentation under `docs/`: getting started, endpoints, authorization, validation, OpenAPI, Native AOT,
  testing and compatibility.

### Changed
- Endpoints whose contract has nothing to validate opt out of the .NET 10 validation filter
  (`DisableValidation()`). It ran on every such request (any `[AsParameters]` contract looked validatable
  to it), costing about 900 bytes and up to half the request time.
- Generated and reflective handlers take the request's `IServiceProvider` instead of `HttpContext`, which
  saves about 200 bytes per request when an endpoint filter runs.
- `GeneratedEndpoints.Activate<T>` takes the request's `IServiceProvider` (generated code only).
- A contract known to the validation resolvers but without any rule no longer documents a `400`.

## [0.4.0-preview.1] - 2026-10-18

### Added
- **API manifest** (`Keel.Endpoints.Testing`): `ApiManifest.Create` describes every endpoint from
  ApiExplorer (route, authorization, parameters, body, responses) with JSON shapes from the app's JSON
  options; `ApiManifest.Compare` classifies each difference as breaking, security or compatible from the
  client's side; `ApiManifest.Verify` checks the app against a committed `api.manifest.json` and fails the
  test (and CI) on any difference. `KEEL_UPDATE_MANIFEST=1|all` accepts intended changes.
- The sample's `api.manifest.json`, verified by the test suite.

### Changed
- `AddKeelEndpoints()` registers ApiExplorer (`AddEndpointsApiExplorer()`), which the manifest and the
  OpenAPI document read.

## [0.3.0-preview.1] - 2026-10-18

### Added
- **Endpoint groups.** An `EndpointGroup` subclass configures every endpoint in its namespace and below
  (authorization, tags, conventions, filters), outermost first, before the endpoint's own `Configure`.
  Works with the generated registration, Native AOT and the reflective registration.
- KEEL006: endpoint groups the generated registration cannot create.
- **Code fixes** for KEEL002 (add the contract member), KEEL003 (`[BindRequest(RequestBinding.Parameters)]`)
  and KEEL004 (`[ValidatableType]`), shipped inside the package.
- **`Keel.Endpoints.Templates`**: `dotnet new keel-api` (optionally `--aot`) and `dotnet new keel-slice`.
- CI job that installs the templates, builds and calls a generated API, and publishes its AOT variant.

## [0.2.0-preview.1] - 2026-10-18

### Added
- **Native AOT.** A source generator writes `app.MapKeelEndpoints(prefix)`: one explicit, typed
  `MapMethods` call per endpoint class, with no reflection. With `PublishAot`, the build writes it to disk
  first so ASP.NET Core's Request Delegate Generator produces AOT-safe binding (`KeelRegistrationFile`).
  Under AOT the .NET 10 validation generator finds contracts without `[ValidatableType]`.
- `[ServerSentEvents(eventType)]`: declares the stream format on the class, visible at compile time.
- KEEL005: endpoint classes the generated registration cannot reach.
- `samples/Keel.AotSample` and a CI job that publishes it natively (warnings are errors) and calls it.

### Changed
- The core library is trimming- and AOT-annotated (`IsAotCompatible`). The reflective
  `MapKeelEndpoints(assembly, prefix)` and `MapEndpoint` are marked `[RequiresUnreferencedCode]` and
  `[RequiresDynamicCode]`, and keep working as before outside AOT.
- The 400 validation response is documented whenever the .NET 10 validation resolvers know the contract.
- The sample serves its OpenAPI document and a Scalar API reference (`/scalar`) in Development, with
  the `X-Api-Key` header declared so requests can be authenticated from the UI.

## [0.1.0-preview.3] - 2026-10-18

### Fixed
- OpenAPI tagged every endpoint with the internal handler type name ("Handlers"). Endpoints are now
  tagged from their namespace (`KeelOptions.TagFromNamespace`, on by default); explicit tags win.

### Added
- The `400` validation problem response is documented for contracts validated by .NET 10 validation
  and for endpoints validated by `WithFluentValidation()`.
- Tests over the ApiExplorer descriptions and, in CI, over the generated OpenAPI document.

## [0.1.0-preview.2] - 2026-10-18

### Added
- `Keel.Endpoints.FluentValidation`: `AddKeelFluentValidation(assembly)` and `WithFluentValidation()`, an
  endpoint filter that validates arguments (and `[FromBody]` members of parameter contracts) with their
  registered `IValidator<T>`, returning `400` validation problem details. Async rules supported.

### Changed
- Releases publish through NuGet Trusted Publishing instead of a stored API key.

## [0.1.0-preview.1] - 2026-10-18

### Added
- Endpoint shapes: `Endpoint<TRequest, TResponse>`, `Endpoint<TRequest>`, `EndpointWithoutRequest<TResponse>`,
  `ResultEndpoint<TRequest, TResult>`, `StreamEndpoint<TRequest, TItem>`.
- Route attributes `[Get]`, `[Post]`, `[Put]`, `[Patch]`, `[Delete]` and `[BindRequest]`.
- `AddKeelEndpoints`, `MapKeelEndpoints`, `MapEndpoint<T>`.
- Native .NET 10 validation through `AddValidation()` and `[ValidatableType]`.
- Fail-closed authorization (`KeelOptions.RequireExplicitAuthorization`, on by default).
- Streaming as a JSON array or Server-Sent Events.
- Analyzers KEEL001 to KEEL004.
- `Keel.Endpoints.Testing` with `EndpointClient`.
