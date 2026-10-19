## What and why

<!-- What changes, and the problem it solves. Link the issue: "Fixes #123". -->

## Checklist

- [ ] Tests cover the change (`dotnet run --project tests/Keel.Endpoints.Tests`)
- [ ] No new warnings (they are errors)
- [ ] `CHANGELOG.md` has a line under the unreleased version
- [ ] Public API change? `PublicAPI.txt` updated and the reason explained above
- [ ] HTTP behavior change? The sample's `api.manifest.json` updated
- [ ] Request path touched? Benchmark run (`dotnet run -c Release --project benchmarks/Keel.Endpoints.Benchmarks`)
