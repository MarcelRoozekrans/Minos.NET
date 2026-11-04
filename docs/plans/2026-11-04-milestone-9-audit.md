# Milestone 9 audit: local and private decisions

| Metric | Value |
|---|---|
| Milestone | 9, Local and private decisions, started 2026-10-21 |
| Audit | 2026-11-04, branch `chore/milestone-9-audit` on `main` at f974539e (release 1.9.0): **PASS** |

**Verdict: PASS.** Every criterion in `MILESTONE.md`'s Definition of Done is met.

| # | Criterion | Verdict | Evidence |
|---|---|---|---|
| 1 | All planned phases complete | Met | Phases 9.1 to 9.5 are `[status: complete]` in `ROADMAP.md`. 9.1 is #255, 9.2 is #297, 9.3 is #304, 9.4 is #307 and #309, and 9.5 is #313 and #314, closed by #316. |
| 2 | All tests pass, including the new projects, the AOT checks and the docs tests; the new packages build with zero IL2 and IL3 warnings and are in the AOT smoke and surface checks | Met | **Local, at f974539e:** the Release build has 0 warnings and 0 errors. All 18 test projects passed, with the live project left out: 4,123 tests, including Recording 187, Evaluation 336, Tool 104, Samples 225, Docs 652, AotSmoke 65, PackTests 68 and Minos.NET.Tests 1,328. **CI on `main` at f974539e:** CI, Docs, Benchmarks, Local systemone and Release Please are green. The 1.9.0 publish run 41831623981 passed `aot-smoke` and `aot-surface`. `samples/Minos.NET.AotSmoke` and `samples/Minos.NET.AotSurface` reference `Minos.NET.Recording`; `RecordingChecks.cs` runs the recorder, the JSONL sink, outcomes and erasure under Native AOT. |
| 3 | `Minos.NET.Recording` records through a decorator that is off unless registered, with tests for redaction, no state in logs, traces or exceptions, outcomes by id, erasure by key, and a failing sink | Met | Phase 9.1, #255. `UseRecordingTests` and `NeverRecordedTests` cover registration. **Redaction before the sink:** `RedactionTests`, including `ARewrittenState_IsWhatTheSinkSees` and `AThrowingRedactor_KeepsNoRecord_AndTheDecisionSucceeds`. **No state in logs, traces or exceptions:** `NoSentinel_ReachesALog_ASpan_AMetricTag_OrAnException` and `TheRecordersOwnExceptions_CarryTheFixedMessages`. **Outcomes by id:** `ScopeTests.ASuppliedId_IsOnTheRecord` and `AGeneratedId_IsAVersion7Guid_OnScopeAndRecord`, `TheInMemorySink_AppliesOutcomesAndErasures`, and the AOT smoke check that attaches a confirmed and a signal outcome to a generated id. **Erasure by key:** `EraseByKey_*` and `AnErasureById_RemovesThatRecord`. **Failing sink:** `AThrowingSink_NeverFailsTheDecision`, `ABlockingSink_DoesNotDelayTheDecision` and `AFailureAfterTheCall_LosesTheRecord_AndNeverTheDecision`. |
| 4 | Recorded decisions export to the Milestone 8 dataset format through `Minos.NET.Evaluation` and `minos`, with confirmed outcomes as expected answers; the export loads in `DatasetRunner` and `minos calibrate`; the record format and outcome schema are documented | Met | Phase 9.2, #297. `RecordedDecisions_ExportLoadAndRun`, `WithoutConfirmedOutcome_IsSkipped` and `ARepeatedDecisionId_IsSkippedSoTheExportStillLoads` in Evaluation.Tests; `Export_*` and `ExportThenCalibrate_Runs` in Tool.Tests. `docs/recording.md` documents the JSONL format and outcomes; `docs/datasets.md` has "Exporting recorded decisions". |
| 5 | Shadow mode runs a candidate in the background, with the primary unchanged when the candidate is slow, failing or over its cap; the report gives per-key agreement, a confusion table and the calibration of both sides, as Markdown and JSON | Met | Phase 9.3, #304. `ShadowModeTests`: `SlowCandidate_PrimaryReturnsAtOnce`, `FailingCandidate_ErrorResult_PrimaryUnchanged_AndErrorKindRecorded` and `CandidateOverCap_PrimaryUnchanged_AndSkippedCapCounted`. The report: `Agreement_PerKey`, `Confusion_IsSparse_AndLabelledByOptionKey`, `Calibration_UsesTheSameItemsOnBothSides` and `ShadowReportOutputTests` for Markdown and JSON; `RecordShadowAndReport_EndToEnd` runs `minos shadow-report`. |
| 6 | A committed shadow report compares a hosted provider with the local model on recorded traffic; each hosted run needs the maintainer's approval | Met | Phase 9.4, #307 and #309. `docs/reports/shadow/banking77/` holds the README, `records.jsonl`, `shadow-report.md` and `shadow-report.json` from run 41292378108: TypeSafe `jev-1.14.0` against `Mapika/decider-2b-GGUF:Q4_K_M`, 400 items, all shadowed. The maintainer approved the run and supplied the `jev-1.14.0` price on 2026-11-01. `ReportFolderTests` rebuilds the report byte for byte from the records. The traffic is replayed Banking77, not production traffic; the README says so. |
| 7 | A guide, "Running decisions locally and privately", is published on the docs site and covers recording, export, shadow mode, the report and switching by configuration | Met | Phase 9.5, #314. `docs/running-locally.md` is deployed by `docs.yml`: https://marcelroozekrans.github.io/Minos.NET/running-locally returns the page titled "Running decisions locally and privately". Its snippets compile and run in `Minos.NET.Docs.Tests`. |
| 8 | `Minos.NET.Recording` is published to nuget.org through the release path | Met | First published as 1.3.0. Release PR #315 merged as f974539e; publish run 41831623981 (`publish-nuget`, Trusted Publishing) pushed all ten packages as 1.9.0 with symbols, and nuget.org lists `Minos.NET.Recording` 1.9.0, the version the guide requires. |

**Code quality reviews:** every phase has a PASS pre-push review:
- `2026-10-22-phase-9.1-pre-push-review.md`;
- `2026-10-28-phase-9.2-pre-push-review.md`;
- `2026-10-31-phase-9.3-pre-push-review.md`;
- `2026-11-01-phase-9.4-pre-push-review.md`;
- `2026-11-01-phase-9.4-report-pre-push-review.md`;
- `2026-11-04-phase-9.5-pre-push-review.md`.

**Release:** skipped. `CONVENTIONS.md` says milestone completion does not tag, because release-please owns releases. v1.9.0 is tagged and published.

**Gaps:** none. Still open by decision, not as gaps:
- #117: the in-process ONNX spike and provider, parked by the milestone design.
- #296: a durable outcome write that callers can wait on, outside this milestone's Definition of Done.
- #118: decision logging and fine-tuning export. Recording and dataset export now cover its logging half; the fine-tuning export is not in this milestone.
- #119: escalation, which is Milestone 10.
- The `.website` submodule PR for this repository's docs has not appeared yet; open .website#127 covers other repositories. The guide is already live on the GitHub Pages site above.
