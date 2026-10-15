# Pre-push review — package validation baseline

| Metric | Value |
|---|---|
| Date | 2026-10-15 |
| Branch | `ci/package-validation-baseline` |
| Base Branch | `origin/main` (e0154ac) |
| Commits Reviewed | 1 (`ci: compare every package with the published 1.0.1, Phase 7.5`) |
| Files Changed | 4, plus this report |
| Lines Added / Removed | 12 / 9 |
| Verdict | **PASS** |

## Plan adherence

The plan is `docs/superpowers/plans/2026-10-13-phase-7.5-publish-1.0.md`, Task 7, Step 5, item 5: "set `PackageValidationBaselineVersion` to `1.0.0` for the six packages, close #28, and mark Phase 7.5 and Milestone 7 complete."

- **Baseline set:** `Directory.Build.targets` adds `PackageValidationBaselineVersion` in the `IsPackable` group, so it applies to all six packages. The comment is updated to match.
- **Deviation, justified:** the baseline is **1.0.1**, not 1.0.0. 1.0.0 was tagged, but nuget.org refused it with "409 The package ID is reserved", so it is not on nuget.org and cannot be a baseline. 1.0.1 is the first published version, from run 38726515640.
- **#28:** closed by the commit's `Closes #28` footer.
- **Phase 7.5:** marked complete in ROADMAP.md and MILESTONE.md, with the evidence.
- **Milestone 7:** completing it waits for `audit-milestone`, which runs after this PR. That follows project-orchestration: the audit comes before `complete-milestone`.
- **Unplanned:** the stale "Publishing blocked" entry is removed from STATE.md's Blockers. Info.

## Code quality

No source code changes; the change is build configuration and docs.

- **Rule 2, YAGNI:** one property, no new mechanism.
- **Rule 7, coverage:** the gate was proven by hand:
  - The baseline restored from `https://api.nuget.org/v3/index.json`, per `~/.nuget/packages/minos.net/1.0.1/.nupkg.metadata`.
  - Making `CachingDecisionClientBuilderExtensions.UseCaching` internal failed the pack with `CP0002 … exists on [Baseline] lib/net10.0/Minos.NET.Caching.dll but not on lib/net10.0/Minos.NET.Caching.dll`. The change was reverted.
  - CI's `build` (Pack step) and `pack-validate` jobs pack every push, so the required checks enforce it.

No findings.

## Commit hygiene

- **Message:** conventional `ci:`, with no scope. `ci` is in `.commitlintrc.yml`'s type list.
- **Release-please:** no parentheses in the body, so its parser cannot drop the commit. `ci` is a hidden changelog section, which is right: this changes no package.
- **Content:** no secrets, no conflict markers, no large or generated files.

No findings.

## Regression tests

- Release build of `Minos.NET.slnx` with `-p:MinosRelease=true`, then pack: 0 errors, six `.nupkg` and six `.snupkg`, all validated against 1.0.1.
- `dotnet test Minos.NET.slnx -c Release --no-build`: every project passed. Live tests skipped (20), as they do without keys.
  - Core: 1283.
  - Docs: 461.
  - Samples: 196.
  - Analyzers: 184.
  - Conformance: 144.
  - OpenAI: 141.
  - Benchmarks: 129.
  - Generator: 123.
  - DI: 80.
  - Caching: 73.
  - AotSmoke: 55.
  - PackTests: 46, in 47 s.
  - Integration: 37.
  - RateLimiting: 18.
- **Info:** the run had `MSBUILDDISABLENODEREUSE=1` set, so it does not re-prove #145's fix. The milestone audit runs the pack tests without it.
- No allocation budgets were affected, because nothing under `src/` changed. No web UI.

## Findings

0 blockers, 0 warnings, 2 info: the 1.0.1 baseline deviation, and the environment variable during the test run.
