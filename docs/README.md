# Keel.Endpoints documentation

| Guide | What it covers |
| --- | --- |
| [Getting started](getting-started.md) | A new API from the template, or Keel in an existing one, in five minutes |
| [Endpoints](endpoints.md) | Endpoint shapes, routes, binding, results, `Configure`, groups, streaming, dependency injection |
| [Authorization](authorization.md) | Fail-closed by default, and every place a decision can be made |
| [Validation](validation.md) | .NET 10 validation, `[ValidatableType]`, FluentValidation, the 400 response |
| [OpenAPI](openapi.md) | Operation names, tags, documented responses, Scalar |
| [Native AOT](native-aot.md) | The generated registration, the two-step build, JSON contexts, limits |
| [Versioning](versioning.md) | A version per folder with Asp.Versioning, unique operation names per version |
| [Idempotency](idempotency.md) | Safe retries for POST and PATCH with an `Idempotency-Key` header |
| [MCP tools](mcp.md) | Endpoints as MCP tools for AI agents: schemas, hints, authorization, how a call runs |
| [Safe actions for agents](agents.md) | The user confirms before a destructive tool runs (MCP elicitation), and every write is audited |
| [Observability](observability.md) | Operation and feature on traces and metrics, validation errors per field, handler timing |
| [Testing](testing.md) | Typed calls with `EndpointClient`, the API manifest, the public API files |
| [API manifest](api-manifest.md) | `api.manifest.json`, what counts as a breaking change, `dotnet keel diff` and the pull request action |
| [Diagnostics](diagnostics.md) | KEEL001 to KEEL006 and their code fixes |
| [Compatibility](compatibility.md) | Versioning, what 1.0 freezes, and what it does not |
| [Trying it locally](testing-locally.md) | Building the repository, the samples and the smoke tests |
