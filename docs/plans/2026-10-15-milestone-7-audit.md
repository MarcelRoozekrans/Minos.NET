# Milestone 7 audit: 1.0 release

| Metric | Value |
|---|---|
| Milestone | 7, 1.0 release, started 2026-10-12 |
| Audit | 2026-10-15, branch `ci/package-validation-baseline` on `main` at e0154ac: **PASS** |

**Verdict: PASS.** Every criterion in `MILESTONE.md`'s Definition of Done is met.

| # | Criterion | Verdict | Evidence |
|---|---|---|---|
| 1 | All planned phases complete | Met | Phases 7.1 to 7.5 are `[status: complete]` in `ROADMAP.md`. Phase 7.5 closes in this branch, after the publish. |
| 2 | All tests pass; the pack tests run locally without hanging (#145) | Met | **Full local run:** 2,970 passed, 0 failed, 20 live skipped. **Pack tests:** run again with `MSBUILDDISABLENODEREUSE` unset, 46 passed in 59 s, with no hang. #145 is closed. **CI:** `ci.yml` on `main` at e0154ac passed, and publish run 38726515640 on v1.0.1 passed `build`, `aot-smoke`, `aot-surface`, `pack-validate` and `publish-nuget`. |
| 3 | Caching and rate-limiting stages (#143), documented, no new core dependency | Met | `Minos.NET.Caching` and `Minos.NET.RateLimiting` ship as their own packages, and `docs/pipeline.md` documents `UseCaching` and `UseRateLimiting`. `Minos.NET.csproj`'s package references are identical to f332ab0, the start of the milestone. #143 is closed. |
| 4 | The whole public API is reviewed, and `PublicAPI.Shipped.txt` describes 1.0 | Met | Phase 7.2, #187; ticked at its close. |
| 5 | Live runs on the final API against TypeSafe, OpenRouter and a local server | Met | Phase 7.3, #189; ticked at its close. |
| 6 | The OpenAI adapter is labelled unverified everywhere, pinned by a docs test | Met | Phase 7.2; ticked. The README's package table says "not yet tested against the live API". |
| 7 | AOT and trim clean, allocation budgets unchanged or tightened, async yielding checks (#104) | Met | Phase 7.4, #191; ticked. `aot-smoke` and `aot-surface` passed again in run 38726515640. |
| 8 | Every package on NuGet through release-please and Trusted Publishing (#29); the guide states the version; the site links to NuGet; api-compat guards later changes (#28) | **Met as amended** | **Published:** all six packages are on nuget.org as 1.0.1, with symbols, pushed by Trusted Publishing from `ci.yml` in run 38726515640. The twelve files are attached to the v1.0.1 release. **Amendment:** 1.0.0 was tagged but never published, because another owner's prefix reservation refused it with 409. **Docs:** the guide reads "This guide describes Minos.NET 1.0.1" and the README links all six packages. **Gate:** `PackageValidationBaselineVersion` 1.0.1 fails the pack on a breaking change; a deliberately internal `UseCaching` produced CP0002. #29 and #28 are closed. |

**Code quality reviews:** every phase has a PASS pre-push review:
- `2026-10-12-phase-7.1-pre-push-review.md`;
- `2026-10-12-phase-7.2-pre-push-review.md`;
- `2026-10-13-phase-7.3-pre-push-review.md`;
- `2026-10-13-phase-7.4-pre-push-review.md`;
- `2026-10-13-phase-7.5-pre-push-review.md`;
- the closing branch: `2026-10-15-package-validation-baseline-pre-push-review.md`.

**Release:** skipped. `CONVENTIONS.md` says milestone completion does not tag, because release-please owns releases.

**Gaps:** none. Still open by decision, not as gaps:
- #161: OpenAI fixtures, which need an OpenAI account.
- #51: the Roslyn workarounds, which wait on dotnet/roslyn#85804 and #77119.
