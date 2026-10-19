# Contributing

Thanks for helping. Issues and pull requests are welcome.

## Before you open a pull request

- **Discuss larger changes first** in an issue. The project is deliberately narrow: a proposal that widens
  the public surface should say what it makes possible that is not possible today, and what it costs
  people who will never use it.
- **Prefer what ASP.NET Core already does.** If a feature can be an ordinary `RouteHandlerBuilder`
  convention, it probably should be.
- **Add a test** for every behavior change, and keep `dotnet build` free of warnings (they are errors).
- **New analyzer rules** get the next free `KEELnnn` id, a section in `docs/diagnostics.md` and a test
  with a positive and a negative case.
- **Public API changes** show up as a failing test against `src/*/PublicAPI.txt`. Update the files with
  `KEEL_UPDATE_PUBLIC_API=1` and say why in the pull request. During the 1.0 freeze only additions are
  accepted, and only with a good reason (see `docs/compatibility.md`).
- **Performance-sensitive changes** (anything on the request path) come with a benchmark run:
  `dotnet run -c Release --project benchmarks/Keel.Endpoints.Benchmarks`.

## Building and testing

```bash
dotnet build
dotnet run --project tests/Keel.Endpoints.Tests
```

Offline (no nuget.org): append `-p:KeelOfflineRoslyn=true` to both commands.

Smoke tests that publish and call real apps (bash; Git Bash works on Windows):

```bash
bash eng/aot-smoke.sh linux-x64        # the Native AOT sample
bash eng/template-smoke.sh linux-x64   # the dotnet new templates, JIT and --aot
```

## Good first issues

- Port the test suite to xUnit v3 (the test bodies map one to one).
- A code fix for KEEL001 (rename one of the two routes) and KEEL005 (make the class internal).
- More `keel-slice` options, for example `--stream` for a `StreamEndpoint`.
- A `dotnet` tool that compares the `api.manifest.json` of two git refs (for release notes).

## Releases

Maintainers tag `vX.Y.Z[-preview.N]`; the release workflow packs and pushes to nuget.org through NuGet Trusted Publishing (no stored API key).

## Code of conduct

This project follows the Contributor Covenant 2.1: see [CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md). Security issues: [SECURITY.md](SECURITY.md).
