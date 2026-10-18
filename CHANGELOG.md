# Changelog

All notable changes are listed here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/)
and the project uses [Semantic Versioning](https://semver.org/).

## [0.2.0-preview.1] - Unreleased

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
