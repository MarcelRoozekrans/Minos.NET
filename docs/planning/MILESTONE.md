# Milestone 8: Choosing a provider and a threshold

**Status:** active
**Started:** 2026-10-17
**Design:** `docs/superpowers/specs/2026-10-17-milestone-8-design.md`

## Goal
A .NET user can measure, on their own labelled data, how accurate and how well calibrated each provider is, and choose a provider and a confidence threshold from evidence. The milestone ships four things:
- a documented JSONL dataset format;
- the `Minos.NET.Evaluation` metrics library;
- the `minos` tool, in the `Minos.NET.Tool` package, with `calibrate` and `conformance`;
- a guide backed by committed reports for TypeSafe Jev, Clef Flash and the local model on a Banking77 sample.

## Definition of Done
- [ ] All planned phases are complete.
- [ ] All tests pass, including the new projects, the AOT checks and the docs tests.
- [ ] The JSONL dataset format is documented on the docs site, with a schema test. A loader reads it and reports line-numbered errors.
- [ ] `Minos.NET.Evaluation` computes, per question kind:
  - accuracy;
  - ECE and a reliability table;
  - a coverage and accuracy threshold sweep;
  - median and p95 latency per decision;
  - cost per 1,000 decisions.

  These are computed on the number users gate on: `Confidence` for Choice and Score, and max(p, 1 − p) for Noul. Each metric is checked against hand-computed values, and the results come out as Markdown and JSON.
- [ ] `Minos.NET.Tool` installs with `dotnet tool install` and runs `minos calibrate` and `minos conformance` against any configured System One provider. It is published to nuget.org through the release path.
- [ ] Calibration reports for TypeSafe, Clef Flash and the local model on the Banking77 sample are committed. Each records its date, provider and model id, dataset version, and the prices it used.
- [ ] A "Choosing a provider and a threshold" guide is published on the docs site and backed by those reports.

**Out of scope:**
- Recalibration and cost-aware decisions: #124.
- OpenAI as a configured provider: it is unverified and there is no account.
- Publishing `Minos.NET.Conformance` as its own package: it ships inside the tool.

## Phases
1. Phase 8.1 — Dataset format, loader and the Banking77 sample [complete]
2. Phase 8.2 — Metrics and reports in Minos.NET.Evaluation [pending]
3. Phase 8.3 — The minos tool [pending]
4. Phase 8.4 — Reports and the guide [pending]

## Audit History
| Date | Verdict | Gaps |
|---|---|---|
