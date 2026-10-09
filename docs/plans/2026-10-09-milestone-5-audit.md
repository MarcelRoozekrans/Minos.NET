# Milestone 5 audit — 1.0 hardening (re-scoped)

**Date:** 2026-10-09
**Verdict:** PASS, with one warning: no pre-push-review reports are on file.
**Scope:** the definition of done in `docs/planning/MILESTONE.md` as re-scoped on 2026-10-09. The 1.0 publish, api-compat and the version statements moved to Milestone 7 (`docs/superpowers/specs/2026-10-09-roadmap-design.md`).
**Audited at:** `main` @ f940524.

| Criterion | Result | Evidence |
|---|---|---|
| All planned phases complete | Pass | ROADMAP.md: Phases 5.1–5.4 are `complete`, with completion dates and evidence. Each has a spec and a plan on disk. |
| All tests pass, with samples in replay mode | Pass | `dotnet test ZeroAlloc.Jev.slnx -c Release` on f940524: 1,897 passed across 10 projects, 0 failed, and the 13 live tests skipped without keys. 0 warnings. `main` CI run 37939272033 is green: build, which includes sample replay, plus aot-surface and aot-smoke. |
| TypeSafe and OpenRouter live suites passed with real keys | Pass | Live smoke run 37925900075 on `main`, 13/13, 2026-10-09. Probe run 37913354273 passed 8/8. |
| Public API reviewed; #67, #23, #24, #25, #85 and #21 resolved | Pass | Phase 5.1 review `docs/plans/2026-10-04-phase-5.1-api-review.md` (R1–R14). All six issues are closed, on 2026-10-04. |
| Published benchmark suite against raw .NET and the official JS and Python SDKs | Pass | Phase 5.2: `docs/performance.md`, from three CI runs with JSON results in `benchmarks/compare/results/`. It is on jev.zeroalloc.net since .website#84 merged on 2026-10-09. |
| Native AOT and trim verification over the whole public API, 0 IL2xxx/IL3xxx | Pass | Phase 5.3: the `aot-surface` job is green on f940524. Entry-point coverage requires a check per public entry point. |
| `jev-latest` and `jev-preview` checked live | Pass | Phase 5.4. TypeSafe answers both aliases with `jev-1.13.0`. OpenRouter answers `jev-latest` and rejects `jev-preview`, which is documented and asserted (#111). |
| Allocation follow-ups #68, #73, #74, #79 closed | Pass | All closed on 2026-10-07. |
| Every allocation budget unchanged or tightened | Pass, with a note | Budgets in `samples/ZeroAlloc.Jev.AotSmoke/AllocationChecks.cs` dropped after ZeroAlloc.Rest 3.2.1: EvaluateRoundTrip 5120 → 4352 B, TypedEvaluateRoundTrip 4224 → 3328 B, and the question-set path below its earlier 4736 B. **Note:** Phase 5.3 added a 16 B tolerance to the yielding disabled-logger check. It is sized from 800 runs to absorb measurement noise, not cost, and #104 tracks replacing it with TestHelpers' async gate. |
| Pre-push reviews on file | **Warning** | No `docs/pre-push-review-*.md` reports exist. Each phase instead had per-task reviews and a final whole-branch review during subagent-driven development, recorded in its SDD ledger, which is git-ignored and removed at the end. No independent pre-push-review report is on file. This is not a blocker. |
| Release will tag correctly | Skipped | `CONVENTIONS.md`: "Milestone completion tags a release: no". Releases are owned by release-please. |

## Gaps

None that block completion. The pre-push-review warning is recorded above. Running `pre-push-review` on each feature branch is recommended from Milestone 6 on, so the next audit has independent reports to point at.
