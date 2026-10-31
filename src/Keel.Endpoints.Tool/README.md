# Keel.Endpoints.Tool

`dotnet keel diff` compares the `api.manifest.json` that
[Keel.Endpoints.Testing](https://www.nuget.org/packages/Keel.Endpoints.Testing) keeps for an API, between two
git refs or two files, and lists what changed for clients: breaking, security and compatible changes.

```bash
dotnet tool install --global Keel.Endpoints.Tool --prerelease

dotnet keel diff v1.0.0                       # since v1.0.0, against the working tree
dotnet keel diff main HEAD --fail-on breaking # exit code 1 on breaking changes
dotnet keel diff v1.0.0 v1.1.0 --format markdown --output api-changes.md
```

| Option | |
| --- | --- |
| `--manifest <path>` | The manifest to compare (repeatable). Default: every `*api.manifest.json` in the repository |
| `--format` | `text` (default), `markdown` (pull request comments, release notes) or `json` |
| `--output <file>` | Write the report to a file |
| `--fail-on` | `breaking`, `security`, `any` or `none` (default) |

Endpoints exposed to AI agents as MCP tools ([Keel.Endpoints.Mcp](https://www.nuget.org/packages/Keel.Endpoints.Mcp))
are compared as tools too: a tool removed or renamed is breaking, a new tool that changes data or one no longer marked
destructive is a security change, and a changed description is shown in full.

On pull requests, the [api-diff GitHub Action](https://github.com/amondejar/keel-endpoints/tree/main/actions/api-diff)
runs it and keeps one comment up to date with the API changes. See the
[API manifest guide](https://github.com/amondejar/keel-endpoints/blob/main/docs/api-manifest.md).
