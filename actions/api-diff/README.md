# Keel.Endpoints API diff action

On every pull request, compares `api.manifest.json` between the base and the head with
[`dotnet keel diff`](../../src/Keel.Endpoints.Tool), keeps **one** comment up to date with the API changes
(breaking, security, compatible), writes the report to the job summary, and can fail the check.

```yaml
name: API diff
on: pull_request

permissions:
  contents: read
  pull-requests: write   # for the comment

jobs:
  api-diff:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: 10.0.x
      - uses: amondejar/keel-endpoints/actions/api-diff@main
        with:
          fail-on: breaking   # breaking | security | any | none (default)
```

| Input | Default | |
| --- | --- | --- |
| `fail-on` | `none` | Fail the step on `breaking`, `security`, `any` |
| `manifest` | all `*api.manifest.json` | Paths to compare, one per line |
| `base` | the pull request's base commit | Any git ref |
| `comment` | `true` | Post or update the pull request comment |
| `version` | latest | `Keel.Endpoints.Tool` version |

Outputs: `breaking` (count) and `report` (path of the Markdown report).

The diff compares committed manifests, so the pull request must include the updated `api.manifest.json`;
the manifest test (`ApiManifest.Verify`) fails the build when it does not. Pull requests from forks get a
read-only token: the report still reaches the job summary, and the comment step is skipped with a warning
if GitHub refuses it.
