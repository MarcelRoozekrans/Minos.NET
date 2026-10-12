# Milestone 6: Provider-neutral core

**Status:** complete
**Started:** 2026-10-09
**Completed:** 2026-10-12
**Design:** `docs/superpowers/specs/2026-10-09-milestone-6-design.md`

## Goal
One typed question set runs against TypeSafe, OpenRouter, OpenAI and a local `/v1/systemone` server by configuration alone, under a vendor-neutral name and behind an `IDecisionClient`. The library stops being a TypeSafe Jev client and becomes a provider-neutral decision client. Its public API reaches the shape that Milestone 7 reviews and freezes. Nothing is published in this milestone.

## Definition of Done
- [x] All planned phases complete.
- [x] All tests pass: unit, generator, analyzer, DI, docs, integration, pack, samples in replay mode, and the AOT smoke and surface checks.
- [x] The name decision is recorded in `docs/planning/`.
  - No public type, namespace, package id or analyzer ID carries the vendor name "Jev". The only exception is a deliberate `ZeroAlloc.Jev` convenience or redirect package, if Phase 6.1 decides on one.
  - The repository and docs site have moved to the new name, or the maintainer's remaining actions are listed in STATE.md.
- [x] The generator's output contains no `/v1/systemone` wire JSON; serialization lives only in protocol adapters. A test serializes one question set for both protocols.
- [x] `IDecisionClient` is the client abstraction, with retries, telemetry and logging as pipeline stages. Every allocation budget on the `/v1/systemone` path is unchanged or tightened.
- [x] Both adapters have recorded-fixture tests. An unsupported question kind, or image state on a provider without image input, fails with a typed error naming the provider and the question, never as a raw HTTP failure.
- [x] Live runs pass against TypeSafe, OpenRouter and OpenAI, and against one local `/v1/systemone` server, either in CI or documented as run.
  - Moved in part to Milestone 7 on 2026-10-12: TypeSafe, OpenRouter and the local server pass; the OpenAI run waits for a key (#161), and Milestone 7's Definition of Done already requires it. Decided after the 2026-10-12 audit.
- [x] The conformance suite runs green against recorded TypeSafe and OpenAI fixtures.
  - Partly met on 2026-10-12: TypeSafe's and OpenRouter's fixtures are recorded and green. OpenAI's success fixtures come from its Decisions guide, because there is no OpenAI key; #161 tracks recording them.
  - Moved in part to Milestone 7 on 2026-10-12: recording OpenAI's fixtures is in Milestone 7's Definition of Done. Decided after the 2026-10-12 audit.
- [x] A Providers docs page shows the presets and the capability matrix. A sample switches providers by configuration only.

## Phases
1. Phase 6.1 — Rename to Minos and move out of the ZeroAlloc org [complete]
2. Phase 6.2 — Neutral question model and adapter boundary [complete]
3. Phase 6.3 — IDecisionClient abstraction and pipeline [complete]
4. Phase 6.4 — /v1/systemone adapter [complete]
5. Phase 6.5 — OpenAI /v1/decisions adapter and image state [complete]
6. Phase 6.6 — Conformance suite [complete]
7. Phase 6.7 — Providers docs and samples [complete]
8. Phase 6.8 — One question set through both protocols [complete]

## Audit History
| Date | Verdict | Gaps |
|---|---|---|
| 2026-10-12 | FAIL | No test serializes one question set for both protocols, closed by Phase 6.8; no live OpenAI run and no recorded OpenAI fixtures, both moved to Milestone 7 (#161). Report: `docs/plans/2026-10-12-milestone-6-audit.md` |
| 2026-10-12 | PASS | None. Re-audit at ec75f56 after Phase 6.8; the OpenAI-key items are in Milestone 7. Report: `docs/plans/2026-10-12-milestone-6-audit.md` |
