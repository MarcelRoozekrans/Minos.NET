# Milestone 10 audit: escalation

| Metric | Value |
|---|---|
| Milestone | 10, Escalation, started 2026-11-04 |
| Audit | 2026-11-07, branch `chore/audit-milestone-10` on `main` at 2dbecd22 (release 1.11.2): **PASS** |

**Verdict: PASS.** Every criterion in `MILESTONE.md`'s Definition of Done is met. One deferred task and one open maintainer action are noted below; neither is a gap.

| # | Criterion | Verdict | Evidence |
|---|---|---|---|
| 1 | All planned phases complete | Met | Phases 10.1 to 10.4 are `[status: complete]` in `ROADMAP.md`. 10.1 is #323, 10.2 is #328, 10.3 is #331, #332 and #334, and 10.4 is #339 and #341. |
| 2 | All tests pass, including the AOT smoke and surface checks and the docs tests, with zero IL2 and IL3 warnings | Met | Local, `MSBUILDDISABLENODEREUSE=1`: Release build 0 warnings and 0 errors; all 18 test projects pass, among them Minos.NET.Tests 1578, Docs 804 and Samples 333. In the full run Benchmarks.Tests failed 49 of 129 on the sandbox's inotify limit of 128 instances; run on its own it passes 129 of 129. CI on `main` at 2dbecd22: CI, Benchmarks and baseline-checks success, and the CI run has `build`, `aot-smoke`, `aot-surface` and `pack-validate` green. Docs success at 48df78c2, the last commit that touched docs. |
| 3 | `UseEscalation` re-asks only the questions below their per-key threshold against a keyed target, merges, skips for time, records the path, leaks no state | Met | `EscalationTests`: `A_Choice_below_its_threshold_is_re_asked`, `A_Score_below_its_threshold_is_re_asked`, `A_Noul_near_one_half_is_re_asked`, `A_Noul_at_0_05_is_not_re_asked`, `A_confident_answer_is_never_re_asked`, `The_escalated_answer_wins_when_less_confident`, `A_failing_target_keeps_the_primary_answers`. `EscalationTimeTests`: `Time_left_below_ExpectedLatency_skips_with_NoTimeLeft`. `EscalationMergeTests`: merged answers equal each client's full answers. The path is on the response (`DecisionPathTests`), the span and counters (`EscalationTelemetryTests`), the logs (`EscalationLoggingTests`) and the record (`RecordedPathTests` in Recording.Tests). `No_state_reaches_logs_tags_or_messages` covers state. Shipped in 1.10.0. |
| 4 | `UseFallback` moves through keyed clients, composes with `UseRetries` and DI, skips a step that does not fit, records the answerer, pins the order with `UseEscalation` | Met | `FallbackTests` (`Each_failed_step_is_listed_before_the_answering_one`, `An_exhausted_chain_returns_the_last_called_steps_error`), `FallbackTimeTests` (`A_step_that_does_not_fit_is_skipped_as_NotCalledNoTimeLeft`), `FallbackRegistrationTests` (`Keyed_steps_resolve_from_the_container`), `RoutingCompositionTests` (`UseRetries_inside_retries_step_zero_before_falling_back`, `Escalation_over_fallback_tells_one_flat_story`, `Fallback_over_escalation_only_reacts_to_the_primary_failing`). Shipped in 1.10.0. |
| 5 | A ticket-triage sample with local-first decisions and a committed report of the split by path, latency and cost; each hosted run approved | Met | `samples/Minos.NET.Samples.TicketTriage`, `TriageStagesTests`. `docs/reports/triage/banking77/` holds `README.md`, `records.jsonl`, `triage-report.json` and `triage-report.md`, from Actions run 42239475037: 400 of 400 decided, 106 of 1200 questions escalated over 104 target calls, with a Latency and a Cost section. The maintainer approved both billed runs; the first (run 42230673543) made no target call and was not committed. |
| 6 | A guide on the docs site teaches confidence routing as a framework feature | Met | `docs/escalation/` has `index`, `thresholds`, `time-budget`, `fallback`, `composing-and-privacy` and `chat-model-target`. The Docs tests, with the figures tied to the committed reports, pass. |
| 7 | A dotnet/extensions proposal draft is in the repository with no private details, and the maintainer has approved it | Met | `docs/plans/2026-11-06-dotnet-extensions-decision-proposal-draft.md`. The design spec reads `Draft approved: 2026-11-07, by the maintainer, who posts it themselves`. Posting it is out of scope by the milestone's own terms. |
| 8 | The new API ships in a minor release through release-please, and ApiCompat is clean | Met | `UseEscalation` (#323) and `UseFallback` (#328) are in v1.10.0, a minor release; its CI run 42054379933 passed `pack-validate` and `publish-nuget`. Package validation runs on every pack against the baseline in `Directory.Build.targets`, and `pack-validate` is green on `main`. |

**Notes, not gaps**
- Phase 10.2 Task 7, token metrics per call, moved to #327 on the maintainer's call. It is blocked on ZeroAlloc-Net/ZeroAlloc.Telemetry#241, which cannot yet tag one measurement per element. The Definition of Done does not name it, and the unticked steps in the plan are deliberate.
- The maintainer posts the dotnet/extensions comment themselves. Before posting, re-check that dotnet/extensions issue 7764 and PR 7795 are still open and that 7795's API has not changed.
- On CPU-only hardware the local model's median is about 4 to 5.5 s, so escalation within Daedalus's 5 s budget is mostly skipped for time. The report and the guide say so.
- Open `.website` bot PR #127 (`chore: update submodules`) changes 26 `repos/*` submodules and none is Minos, so no approval question applies. Ask the maintainer before approving or merging a bot PR that includes the Minos docs.

**Code quality reviews:** every phase has a PASS pre-push review:
- `2026-11-05-phase-10.1-pre-push-review.md`;
- `2026-11-05-phase-10.2-pre-push-review.md`;
- `2026-11-05-phase-10.3-pre-push-review.md`;
- `2026-11-06-phase-10.3-report-pre-push-review.md`;
- `2026-11-07-phase-10.4-pre-push-review.md`.

**Release:** skipped. `CONVENTIONS.md` says milestone completion does not tag, because release-please owns releases. v1.10.0 carries the new API and v1.11.2 is the latest release.
