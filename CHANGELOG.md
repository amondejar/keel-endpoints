# Changelog

All notable changes are listed here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/)
and the project uses [Semantic Versioning](https://semver.org/).

## [0.1.0-preview.2] - Unreleased

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
