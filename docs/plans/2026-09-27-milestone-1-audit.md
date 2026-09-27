# Milestone 1 Audit — Foundation & core client

**Date:** 2026-09-27
**Verdict:** PASS, with two notes

| Criterion | Result | Evidence |
|---|---|---|
| All planned phases complete | PASS | Phases 1.1–1.8 are complete in ROADMAP.md. Phase 1.8 merged as PR #44. |
| All tests passing | PASS | `dotnet test ZeroAlloc.Jev.slnx -c Release`: unit 231, generator 57, integration 15 and pack 4 pass; 6 live tests skipped without `JEV_LIVE=1`. CI `build` is green on #44. |
| Live smoke when a key is present | PASS, with a note | The OpenRouter live evaluation passed on 2026-09-27. The TypeSafe live tests have not run yet, because no TypeSafe key is set locally or in the `live-api` environment. |
| `JevClient` calls both endpoints and returns `Result<T, JevError>` for every documented error | PASS | Phase 1.4, with WireMock coverage of 401, 422, 429, 5xx, time-out and network failure added in 1.7. |
| The generator emits question JSON and parses answers against the wire fixtures | PASS | Phase 1.3. Snapshot tests, plus a generated set round-tripping through the AOT smoke app, the benchmarks and the live suite. |
| 429/529 retried with backoff that honours `retry-after` | PASS | Phase 1.6, with `retry-after-ms` precedence. Over real HTTP, `Retry-After: 1` is honoured (1.7). |
| The AOT smoke app has zero IL2xxx/IL3xxx warnings in CI | PASS | Phase 1.8 sets `TreatWarningsAsErrors` in the smoke app, which closed #30. `aot-smoke` is green on #44. |
| BenchmarkDotNet smoke gate and `AllocationGate` budgets in CI | PASS | Phase 1.8: `smoke / benchmarks` and the five allocation gates, green on linux-x64. |
| release-please wired; NuGet publishing deferred (#29) | PASS | `release-please.yml` has no publish job. Release PR #33 stays open. |
| Pre-push reviews on file | Note | There are no `docs/pre-push-review-*.md` reports. Every phase instead ran a review after each task and a final whole-branch review, with a fix round; each plan's Outcome section records them. |
| The release will tag correctly | Skipped | CONVENTIONS sets `Milestone completion tags a release: no`, because release-please owns releases. |

## Follow-ups carried forward
- Run the TypeSafe live suite once a key is available: create the `live-api` environment, dispatch **Live smoke**, and record whether TypeSafe sends `Retry-After` and what the 422 body looks like.
- #40 (`X-TypeSafe-Retry-Count`) and removing the `JevClient.ThrowDeclined` workaround. Both are unblocked by ZeroAlloc.Rest 2.2.0 and ZeroAlloc.Resilience 3.3.0.
- #28 api-compat and #29 NuGet publishing wait until the package is declared mature.
