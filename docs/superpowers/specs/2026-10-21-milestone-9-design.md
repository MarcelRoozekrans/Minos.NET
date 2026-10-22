# Milestone 9 Design — `Local and private decisions`

**Date:** 2026-10-21
**Milestone:** `9`
**Stage:** milestone (one milestone, multiple phases)

> This revises Milestone 9 of the roadmap design (`docs/superpowers/specs/2026-10-09-roadmap-design.md`), "Hosted to local". It implements #118.
>
> #117, the in-process ONNX inference spike and provider, leaves this milestone and stays parked: see Decisions.

## Goal

A .NET team can run its decisions on a local decision server, offline and with private data never leaving the machine, and can show from its own traffic that the local model decides well enough before it switches.

The path is:
1. record real decisions, with the outcomes that become known later;
2. export them as a Milestone 8 dataset;
3. shadow the local model beside the hosted provider without affecting the app;
4. read the agreement and calibration report;
5. switch provider in configuration.

Minos already talks to a local server, through the `Custom` and `VllmSemanticRouter` presets, so switching needs no new client code.

**The first real workload is Daedalus.NET.** It makes one decision, `NodeQuestions`: one Score question and six Noul questions, asked 5 to 15 times per manufacture run. Today that decision is switched off, because the task text it sends may only go to a zero-data-retention account. A local model removes that constraint.

## Decisions

Made by the maintainer on 2026-10-21:
- **Why local:** the reason is offline use and privacy, not cost. Hosted decisions cost about $0.0001 each.
- **The in-process ONNX provider (#117) stays parked**, outside this milestone.
  - **A separate local decision server is fine.** Minos already supports it, CI already runs one, and Milestone 8 measured one.
  - **What in-process would add:** no extra process to install, which matters for desktop, mobile or single-binary apps.
  - **What it would cost:**
    - a C# tokenizer, decision head and calibration;
    - native ONNX Runtime libraries proven under Native AOT;
    - model files shipped with every app.
  - **When to revisit:** when an app cannot run a separate server.
- **Which provider an app uses is a configuration choice**, through `Minos:Active`, as today. Automatic fallback from hosted to local when offline belongs to Milestone 10, Escalation, which builds provider chains.
- **Daedalus is the first workload.** Its requirements, from its maintainer session on 2026-10-21, are binding for the recorder and shadow mode:
  - The recorder is off by default.
  - It decorates the app's existing `IDecisionClient` registration and never replaces it.
  - It never writes the request state to logs, traces or exception messages.
  - A redaction hook runs before anything is persisted or exported.
  - It supports deleting records by the app's own key, for GDPR erasure.
  - **Storage is a pluggable sink.** Daedalus stores records in its own Postgres. A JSONL export from that store is fine; loose JSONL on the host is not the storage.
  - **Records carry an id the app can use.** Outcomes are attached later by decision id. The app either supplies its own id, such as Daedalus's `run:seq`, or stores the one Minos returns.
  - **Shadow mode stays off the decision's path.** It is fire and forget, outside the caller's budget, and its failures are invisible to the caller. A paid hosted candidate needs a daily cap.
  - **Every new package is Native AOT clean:** zero IL2 and IL3 warnings, and no reflection-based System.Text.Json. Daedalus's CI ratchet blocks increases.
  - **Nothing adds latency** to the primary decision path, beyond writing to the sink.

My calls:
- **The packages, split by when they run** (settled after the builder check):
  - **`Minos.NET.Recording` is runtime only.** It holds the recorder decorator, the shadow decorator, the record and outcome types, the sink abstraction, the redaction hook and erasure. It depends on `Minos.NET` only, so an app takes nothing it does not run.
  - **What ships in it:** a JSONL file sink and an in-memory sink, for development and tests.
  - **What apps write themselves:** database sinks, such as Daedalus's Postgres one.
  - **The offline side goes in `Minos.NET.Evaluation`,** which is already AOT clean and in the AOT surface checks. It gains the export to the dataset format and the shadow report, and `minos` gains the commands that run them. They read records in the documented record file format, not through the sink.
  - **A `Minos.NET.Recording.DependencyInjection` split is the builder's call**, following how `Minos.NET.DependencyInjection` is split from the core.
- **Two kinds of outcome.** A **confirmed** outcome says what the right answer was, for each question key. A **signal** outcome says something happened, such as "review rejected" or "node failed". The export turns confirmed outcomes into the dataset's expected answers. Signals stay with their record, so a report can count them, but they never become an expected answer.
- **What the shadow report holds:**
  - agreement between the primary and the candidate, per question key;
  - the candidate's calibration against confirmed outcomes, where there are any;
  - for Score and Choice, a confusion table of primary against candidate.

  It reuses `Minos.NET.Evaluation`'s metrics and output formats, and `minos` gains a command that builds it from exported records.

## Settled after the builder check (my calls)

1. **One record per decision holds everything.** A record holds:
   - the decision id and the app's optional erasure key;
   - the time, provider, requested model and served model, the request id, latency and cost;
   - the question set's key, version and definition;
   - the state, after redaction;
   - the primary's answers, with probabilities and confidences;
   - when shadowed, the candidate's provider, model, answers, latency and error kind;
   - its outcomes, added later.

   Answers are model output and do not pass through the redaction hook. Only the state does.
2. **Shadow mode builds on the recorder.** A shadowed decision is a record with candidate answers, so shadow mode needs a sink. It is a second opt-in, on top of recording.
3. **Erasure uses a key the app chooses.** It is separate from the decision id. Daedalus would use its run id, for example. The app sets the key per call, the record stores it, and the sink deletes every record that has it. The 9.1 spec also allows deleting one record by its decision id.
4. **Small additive changes to `Minos.NET` are allowed**, if 9.1 needs them to carry a decision id or an erasure key per call. One example is a property on `DecisionRequest`. They are new public API, reviewed in the 9.1 spec, and released as a minor version.
5. **The daily cap is a count of calls per candidate, kept in memory for each process,** with a day boundary in UTC. Over the cap, the candidate is skipped and the skip is counted in telemetry. A cap that survives restarts is out of scope.
6. **The shadow report calibrates both the primary and the candidate** against confirmed outcomes, so a hosted-versus-local comparison can say which is better calibrated. It also gives their agreement.
7. **9.4's traffic is a sample app.** It replays the Banking77 set through the recorder and the shadow decorator, and attaches the known labels as confirmed outcomes once each decision returns. Real Daedalus traffic is not available, because Daedalus's decisions are off until it adopts this. The report says the traffic was replayed.
8. **9.4 runs the 400-item Banking77 set.**
   - The primary is TypeSafe, which needs the maintainer's approval to start, and the candidate is the local model.
   - It runs in a dispatch-only job with a long time limit, because the local model takes about 6.5 s per decision on the CPU runner, about 45 minutes in all.
   - The hosted cost is a few cents.
9. **Extending the AOT smoke app and the surface checks to the new package is part of 9.1.**

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

## Phases

1. **Phase 9.1: The decision recorder** — `Surface: Backend`
   - **Goal:** an opt-in `IDecisionClient` decorator in `Minos.NET.Recording`, with a pluggable sink, a redaction hook, decision ids, confirmed and signal outcomes, and erasure, AOT clean, with file and in-memory sinks (#118).
2. **Phase 9.2: Export to the dataset format** — `Surface: Data`
   - **Goal:** in `Minos.NET.Evaluation` and `minos`, turn recorded decisions and their confirmed outcomes into Milestone 8 dataset JSONL, document the record file and outcome schema, and prove the export runs through `minos calibrate` (#118).
3. **Phase 9.3: Shadow mode and its report** — `Surface: Backend`
   - **Goal:** a background candidate decorator in `Minos.NET.Recording`, off the decision's path, with a daily cap, writing candidate answers to the record. Plus the agreement and calibration report from recorded pairs, in `Minos.NET.Evaluation` and `minos`, with Milestone 8's code (#118).
4. **Phase 9.4: A shadow report on recorded traffic** — `Surface: Data`
   - **Goal:** record traffic, shadow the local model against a hosted provider, and commit the report. The traffic is a sample app replaying the Banking77 set through the recorder, with the local model on the CI server, and hosted runs need the maintainer's approval.
5. **Phase 9.5: "Running decisions locally and privately" guide** — `Surface: Docs`
   - **Goal:** the record, export, shadow, report and switch path as one guide, with the local server setup, backed by Phase 9.4's report (#118).

## Dependencies on Prior Milestones

- **Milestone 8:**
  - the dataset format and loader;
  - `Minos.NET.Evaluation`'s metrics and report writers;
  - the `minos` tool;
  - the local server in `local-systemone.yml`;
  - `calibrate.yml`, for billed hosted runs started by the maintainer.
- **Milestone 6:** the `IDecisionClient` abstraction and its decorator pipeline, plus the `Custom` and `VllmSemanticRouter` presets.
- **External:**
  - Daedalus adopting the recorder needs it to upgrade from Minos 1.1.0. That is Daedalus's work, not part of this milestone's Definition of Done.
  - The local server is ollajev 0.4.0, pinned to Python below 3.13 in Renovate.

## External Constraints

- **Native AOT:** Daedalus is moving Thalos and Daedalus to Native AOT now, so the new packages must be AOT clean from their first release.
- **Privacy:** recorded state can hold customer text. Nothing may persist it before the redaction hook, and erasure must work through the sink.
- **Hosted runs are billed.** Each one needs the maintainer's approval, as in Milestone 8.

## Risk Areas

| Risk | Impact | Mitigation |
|---|---|---|
| Recorded state leaks into logs, traces or exception text | Private data exposed, and the recorder cannot be adopted | Tests assert that no log, span or exception carries the state, using a sentinel string, alongside the existing "what it never records" rule. |
| Shadow mode slows or breaks the primary path | It cannot be used in production | A bounded background queue that drops when full. Tests use a slow and a failing candidate, and assert the primary's latency and result are unchanged. |
| Signal outcomes are weak labels | The report overstates what the candidate gets wrong | Signals never become expected answers. Reports count them separately, and the guide explains the difference. |
| The local model on a CPU runner is slow, about 6.5 s per decision | The 9.4 runs take long | A dispatch-only job with a long limit, as in Milestone 8, and an item count that fits it. |
| A new public API in a new package freezes too early | Breaking changes later | The package versions with the rest, with a package validation baseline from its first release, and an API review in the 9.1 and 9.3 specs. |

## Open Questions

None blocking. These are decided inside their phases:
- **9.1:** how a decision id and an erasure key reach the recorder, and the id gets back to the caller, without changing `IDecisionClient`'s return type. Options include an ambient recording scope, a request property, and an id supplied by the caller. Settled item 4 bounds the change.
- **9.1:** whether the DependencyInjection registration ships in its own package.
- **9.2:** the signal outcomes travel in a companion file. The dataset format rejects unknown members, so putting them in the dataset file would break version 1. The 9.2 spec decides the companion file's shape.
