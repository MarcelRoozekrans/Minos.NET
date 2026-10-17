# Milestone 8 audit: choosing a provider and a threshold

| Metric | Value |
|---|---|
| Milestone | 8, Choosing a provider and a threshold, started 2026-10-17 |
| Audit | 2026-10-17, branch `chore/complete-milestone-8` on `main` at a946a5a: **PASS** |

**Verdict: PASS.** Every criterion in `MILESTONE.md`'s Definition of Done is met.

| # | Criterion | Verdict | Evidence |
|---|---|---|---|
| 1 | All planned phases complete | Met | Phases 8.1 to 8.4 are `[status: complete]` in `ROADMAP.md`. 8.1 is #224, 8.2 is #229, 8.3 is #231 with #233, and 8.4 is #234 and #235. |
| 2 | All tests pass, including the new projects, the AOT checks and the docs tests | Met | **Local, at a946a5a:** all 17 test projects passed, with the live project skipped. That includes Evaluation 251, Tool 68, Docs 538, PackTests 55 and Minos.NET.Tests 1303. The Banking77 build tests, the release-script tests and the AOT smoke app also passed. **The flaky test:** `main`'s CI failed at 7bfda34 on `Utf8_state_costs_one_copy_per_call`, which fails depending on test order. #236 fixed the test at its root, and CI on `main` reruns at a946a5a. The upstream half is ZeroAlloc-Net/ZeroAlloc.TestHelpers#94. |
| 3 | The JSONL dataset format is documented on the docs site, with a schema test, and a loader that reports line-numbered errors | Met | Phase 8.1, #224. `docs/datasets.md` documents the format. `DatasetLoadTests` and `DatasetProblemTests` pin the loader's problems, each with its line. |
| 4 | `Minos.NET.Evaluation`: per-kind accuracy, ECE and reliability, a threshold sweep, latency, cost per 1,000, checked against hand-computed values, as Markdown and JSON | Met | Phase 8.2, #229. `CalibrationMetricsTests` checks the metrics against values worked out by hand, and `ReportOutputTests` snapshots the Markdown and JSON. Metrics use `Confidence` for Choice and Score, and max(p, 1 − p) for Noul. |
| 5 | `Minos.NET.Tool` installs with `dotnet tool install`, runs `minos calibrate` and `minos conformance` against any configured System One provider, and is published through the release path | Met | Phase 8.3, #231. `Minos.NET.Tool` and `Minos.NET.Evaluation` were published to nuget.org as 1.2.0 on 2026-10-17. The calibration workflows installed 1.2.0 from nuget.org and ran it against TypeSafe, OpenRouter and a local server. |
| 6 | Calibration reports for TypeSafe, Clef Flash and the local model on the Banking77 sample are committed, recording date, provider and model id, dataset version and prices | Met | Phase 8.4, #235. There are six reports in `docs/reports/`, from runs 39053318208, 39053417194, 39053418745 and 39053226413, with no failed request. `ReportFolderTests` pins each folder's run, checks its README against the report header, and rebuilds every report byte for byte from its results, the dataset and `prices.json`. |
| 7 | A "Choosing a provider and a threshold" guide published on the docs site and backed by those reports | Met | Phase 8.4, #235. `docs/choosing-a-provider.md` is built by `docs.yml` on `main`. `ProviderGuideTests` checks every table cell, sweep row, interval and the run date against the reports. |

**Code quality reviews:** every phase has a PASS pre-push review:
- `2026-10-17-phase-8.1-pre-push-review.md`;
- `2026-10-17-phase-8.2-pre-push-review.md`;
- `2026-10-17-phase-8.3-pre-push-review.md`;
- `2026-10-17-phase-8.4-part-1-pre-push-review.md`;
- `2026-10-17-phase-8.4-part-2-pre-push-review.md`.

**Release:** skipped. `CONVENTIONS.md` says milestone completion does not tag, because release-please owns releases. Release PR #237, for 1.2.1, is open for the maintainer.

**Gaps:** none. Still open by decision, not as gaps:
- #230: keep a run's finished, paid results when it is cancelled or times out. This needs a decision on the runner's cancellation contract.
- ZeroAlloc-Net/ZeroAlloc.TestHelpers#94: `AllocationGate` 1.8.0 can still measure Tier0 code. Minos's tests no longer depend on the gate reaching optimised code.
- #124: recalibration and cost-aware thresholds, outside this milestone.
- #161: OpenAI as a provider, which needs an OpenAI account.
