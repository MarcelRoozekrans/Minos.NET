# Milestone 9: Local and private decisions

**Status:** active
**Started:** 2026-10-21
**Design:** `docs/superpowers/specs/2026-10-21-milestone-9-design.md`

## Goal
A .NET team can run its decisions on a local decision server, offline and with private data never leaving the machine, and can show from its own traffic that the local model decides well enough before it switches. The path:
- record real decisions, with the outcomes that become known later;
- export them as a Milestone 8 dataset;
- shadow the local model beside the hosted provider without affecting the app;
- read the agreement and calibration report;
- switch provider in configuration, through the `Custom` or `VllmSemanticRouter` preset that Minos already has.

Daedalus.NET is the first workload, and its requirements are binding: see the design.

## Definition of Done
- [ ] All planned phases are complete.
- [ ] All tests pass, including the new projects, the AOT checks and the docs tests. The new packages build with zero IL2 and IL3 warnings and are in the AOT smoke and surface checks.
- [ ] `Minos.NET.Recording` records decisions through a decorator that is off unless registered. Tests cover each of these:
  - The redaction hook runs before the sink sees a record.
  - The state never appears in a log, a trace or an exception message.
  - Outcomes, both confirmed and signal, attach by decision id, whether the app supplied the id or Minos generated it.
  - Records can be erased by the app's key.
  - A failing sink never fails the decision.
- [ ] Recorded decisions export to the Milestone 8 dataset format through `Minos.NET.Evaluation` and `minos`, with confirmed outcomes as expected answers, and the export loads in `DatasetRunner` and `minos calibrate`. The record file format and the outcome schema are documented.
- [ ] Shadow mode runs a candidate provider in the background. Tests cover each of these:
  - The primary's result and latency are unchanged when the candidate is slow, failing or over its daily cap.
  - The shadow report gives per-key agreement, a confusion table for Score and Choice, and the calibration of both the primary and the candidate against confirmed outcomes, as Markdown and JSON.
- [ ] A committed shadow report compares a hosted provider with the local model on recorded traffic. Each hosted run needs the maintainer's approval.
- [ ] A guide, "Running decisions locally and privately", is published on the docs site. It covers recording, export, shadow mode, the report and switching by configuration, using a local decision server.
- [ ] `Minos.NET.Recording` is published to nuget.org through the release path.

**Out of scope:**
- The in-process ONNX inference spike and provider: #117, parked until an app cannot run a separate server.
- Automatic fallback from hosted to local: Milestone 10, Escalation.
- Database sinks: apps write their own, such as Daedalus's Postgres sink.

## Phases
1. Phase 9.1 — The decision recorder [complete]
2. Phase 9.2 — Export to the dataset format [active]
3. Phase 9.3 — Shadow mode and its report [pending]
4. Phase 9.4 — A shadow report on recorded traffic [pending]
5. Phase 9.5 — "Running decisions locally and privately" guide [pending]

## Audit History
| Date | Verdict | Gaps |
|---|---|---|
