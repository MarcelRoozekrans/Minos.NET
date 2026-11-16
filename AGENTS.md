# AGENTS.md: Minos.NET

Minos.NET is an unofficial .NET client for decision models: typed questions (Noul, Choice, Score) in, calibrated
typed answers out. It ships ten NuGet packages: the provider-neutral core `Minos.NET` (with the `[Questions]`
source generator and the MIN analyzers bundled in), the provider clients `SystemOne`, `OpenAI` and
`Extensions.AI`, the pipeline stages `Caching`, `RateLimiting` and `Recording`, `DependencyInjection`,
`Evaluation`, and the `minos` dotnet tool (`Minos.NET.Tool`). It runs under Native AOT.

## Layout

- `src/Minos.NET.Generator`, `Minos.NET.Analyzers` and `Minos.NET.CodeFixes` are Roslyn components
  (`netstandard2.0`, not packable). Their DLLs are packed into `Minos.NET`'s `analyzers` folder. Tests and
  samples reference them with `OutputItemType="Analyzer"`.
- `src/Minos.NET.Conformance` and `src/Minos.NET.Extensions.AI.Decisions` are not packable. `Extensions.AI.Decisions`
  is built only in the Decisions configuration, below.
- `samples/`: `AotSmoke` and `AotSurface` are the Native AOT checks; the others replay checked-in recordings offline.
- `docs/` holds the user docs, built by Docusaurus. `docs/design`, `planning`, `plans` and `superpowers` are
  working notes that `mdsnippets` skips. `tools/release` has the Python release scripts.

## Build and test

Prerequisites: `global.json` is the only pin: SDK 11.0.100 with `rollForward: latestMinor`. Libraries and their
tests target `net10.0;net11.0`, so running tests needs the .NET 10 and the .NET 11 runtimes; CI installs
`10.0.x` and `11.0.x` beside the pinned SDK. Application projects, and the tests of applications, target only
`net11.0` (`MinosAppTargetFramework`): `Docs.Tests`, `Samples.Tests`, `Benchmarks.Tests`, `PackTests` and
`AotSmoke.Tests`. Python 3 runs the scripts; `clang` and `zlib1g-dev` the AOT publish.

Job `build` in `.github/workflows/ci.yml`:

```bash
dotnet restore Minos.NET.slnx
dotnet build Minos.NET.slnx --no-restore -c Release
dotnet test Minos.NET.slnx --no-build -c Release --logger "trx" --collect "XPlat Code Coverage" --settings coverage.runsettings --results-directory ./test-results
python3 -m unittest discover -s datasets/banking77 -p "test_build.py"
dotnet tool restore && dotnet mdsnippets && git diff --exit-code
dotnet pack Minos.NET.slnx --no-build -c Release -o ./nupkg
```

The coverage gate (`ci-baseline/actions/coverage-gate`, 80 percent line coverage on the merged Cobertura reports)
is a CI action; the commands above do not reproduce it. The same job also runs every sample with a recording
offline, as a shell loop over `samples/*/recordings.json` in the workflow, with `OPENROUTER_API_KEY` and
`TYPESAFE_API_KEY` set to empty.

Other CI jobs:

- `aot-smoke`: `dotnet publish samples/Minos.NET.AotSmoke/Minos.NET.AotSmoke.csproj -r linux-x64 -c Release -o ./aot-out`,
  then run `./aot-out/Minos.NET.AotSmoke`. It enforces the allocation budgets of the client's hot paths.
- `aot-surface`: `dotnet publish samples/Minos.NET.AotSurface/Minos.NET.AotSurface.csproj -r linux-x64 -c Release -o ./aot-surface-out`.
  The publish is the check.
- `pack-validate`: `dotnet pack Minos.NET.slnx -c Release -p:MinosRelease=true -o artifacts/packages`, then
  `python3 -m unittest discover -s tools/release -p "test_*.py"` and
  `python3 tools/release/inspect_packages.py artifacts/packages "$PACKAGE_VERSION"`, with `PACKAGE_VERSION` the `"."`
  entry of `.release-please-manifest.json`: `PACKAGE_VERSION=$(jq -r '."."' .release-please-manifest.json)`.
  `tests/Minos.NET.Docs.Tests/WorkflowWiringTests.cs` pins these commands; change them together.
- `release-tracking`, release PRs only: `python3 tools/release/ship_release_tracking.py --check`.
- `benchmarks.yml`, job `smoke`: `dotnet build benchmarks/Minos.NET.Benchmarks -c Release`, then
  `dotnet run --project benchmarks/Minos.NET.Benchmarks -c Release --no-build -- --filter "${FILTER:-*}" --iterationCount 1 --warmupCount 1 --invocationCount 1 --unrollFactor 1`.
  List them with `-- --list flat`. The benchmark project runs on the .NET 10 runtime.
- `benchmarks-compare.yml`, job `smoke`: `pip install -r benchmarks/compare/requirements-test.txt`, then
  `python -m pytest benchmarks/compare/tests` and `benchmarks/compare/run.sh --smoke --machine ci`.
- `upstream-decisions.yml`: the Decisions configuration, below.
- `live-smoke.yml`, `calibrate.yml` and `local-systemone.yml` call live providers. `Minos.NET.Live.Tests`
  skips its live facts unless `MINOS_LIVE=1` and the provider key are set.

Inner loop, not run by a CI job, one test class:

```bash
dotnet test tests/Minos.NET.Tests -c Release --filter "FullyQualifiedName~DecisionClientRetryTests"
```

### Decisions configuration

`Minos.NET.Extensions.AI.Decisions` and its tests compile against the pinned upstream `dotnet/extensions`
abstractions. Job `upstream-decisions` runs `bash tools/upstream/build-decisions.sh` (needs `jq` and `git`),
`dotnet build Minos.NET.slnx -c Decisions` and
`dotnet test tests/Minos.NET.Extensions.AI.Decisions.Tests -c Decisions --no-build`. Do not add the
`Microsoft.Extensions.AI.Abstractions` package to a project that imports `tools/upstream/UpstreamDecisions.props`.
`tools/upstream/README.md` still names SDK 10.0.403; `global.json` is what holds.

## Conventions and gotchas

- The version is not set in a project file. `Directory.Build.props` reads `.release-please-manifest.json`
  and builds `X.Y.Z-local`. Only a release build passes `-p:MinosRelease=true`, and it must reach both the
  build that compiles the DLLs and the pack, or a package ships a release number over `-local` DLLs.
- `TreatWarningsAsErrors` is on, with `RS0016` and `RS0017` always errors. Every library with a
  `PublicAPI.Shipped.txt`, which includes `Extensions.AI.Decisions`, also has a `PublicAPI.Shipped.txt` and `PublicAPI.Unshipped.txt`. Put new public API in
  `Unshipped`; do not edit `Shipped`. The analyzers keep `AnalyzerReleases.*.md` the same way. Release PRs
  move the entries with `tools/release/ship_release_tracking.py`.
- Package validation compares each package with its first nuget.org version. The baseline is 1.0.1, set in
  `Directory.Build.targets`; `Evaluation` (1.2.0), `Recording` (1.3.0) and `Extensions.AI` (1.5.0) set their own
  `PackageValidationBaselineVersion` in their csproj. Raise a baseline only at a major release.
- An application project clears `TargetFrameworks` and sets `TargetFramework` to
  `$(MinosAppTargetFramework)`; `dotnet publish` refuses `TargetFrameworks`.
- Exception to the repo-wide `net10.0;net11.0` default: `benchmarks/Minos.NET.Benchmarks` and
  `Minos.NET.Benchmarks.Compare` target `net10.0` only, because BenchmarkDotNet 0.15.8 cannot run on
  `net11.0`. Move them to `MinosAppTargetFramework` once a stable release supports it.
- Package versions are central, in `Directory.Packages.props`. A `10.0.0` floor on a Microsoft.Extensions
  package is deliberate, so every .NET 10 consumer resolves; read its comment before raising it.
- The generator and analyzers compile against Roslyn 5.0.0 (the first .NET 10 compiler) on purpose.
- Every C# block in `docs/` is generated from `tests/Minos.NET.Docs.Tests` by `mdsnippets`. Edit the test
  source and rerun `dotnet mdsnippets`; do not edit a block in the Markdown.
- Commit scopes are limited by `.commitlintrc.yml`: `core`, `generator`, `di`, `systemone`, `openai`,
  `ratelimiting`, `caching`, `recording`, `conformance`, `tests`, `benchmarks`, `samples`, `docs`, `ci`, `deps`
  and a few more. A scope such as `evaluation` is not allowed.

<!-- org-core:start sha256=08a88996bc9165f888fc0260607cfd0670cae128c323e16707c6b452ee66222d (generated from docker-nuc scripts/agents-md/org-core-personal.md; do not edit here) -->
## Org rules

These rules hold in every public MarcelRoozekrans repo.

### Commits and releases

- Conventional Commits. release-please reads them on the default branch, and the changelog sections
  in `release-please-config.json` decide which types count. The standard: `feat`, `fix`, `perf`,
  `deps` and `revert` open or update the release PR; `docs`, `test`, `refactor`, `build`, `ci`,
  `chore` and `style` do not, unless the commit is breaking or has a `Release-As:` footer. The
  release happens when that PR merges. A breaking change needs `!` or a `BREAKING CHANGE:` footer.
- A commit body never contains nested parentheses. release-please cannot parse them and silently
  drops the whole commit from the changelog, while the workflow still reports success.
- After merging a commit that should release, check that it appears in the release-please PR.

### Branches and pull requests

- Every change goes through a branch and a pull request. Never push to the default branch directly.
- A PR body says what the diff does, and nothing the diff does not do.
- Fix review findings in the PR under review, including small ones. File an issue only for a finding
  that belongs to other code, needs a design decision, is its own piece of work, or waits on
  something external.

### CI

- The .NET repos call the shared workflows of `MarcelRoozekrans/ci-baseline`, pinned to `@v1`, never
  `@main`.
- Its coverage gate fails the build when line coverage drops below the floor of 80%, and Codecov
  fails a PR that lowers project coverage by more than 1 percentage point. New code comes with
  tests.
- A repo with a lower floor documents the reason and links the issue that raises it.
<!-- org-core:end -->
