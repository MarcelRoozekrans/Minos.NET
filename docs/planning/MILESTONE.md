# Milestone 10: Escalation

**Status:** active
**Started:** 2026-11-04
**Design:** `docs/superpowers/specs/2026-11-04-milestone-10-design.md`

## Goal
A .NET app decides on a local decision server first and hands only the questions the local model is unsure of to a stronger decider, within the time it has. When a provider fails, the next one in a fallback chain answers. Each answer says which path produced it. Both are `IDecisionClient` pipeline stages in the core, `UseEscalation` and `UseFallback`, that compose with DI and keyed clients. A ticket-triage sample shows the split between local and escalated answers, and a guide teaches confidence routing as a framework feature (#119).

Daedalus.NET is the first workload, and its requirements are binding: see the design.

## Definition of Done
- [ ] All planned phases are complete.
- [ ] All tests pass, including the AOT smoke and surface checks and the docs tests, with zero IL2 and IL3 warnings.
- [ ] `UseEscalation` re-asks only the questions below their per-key threshold, against a keyed target client, and merges the answers. Tests cover each of these:
  - Score, Choice and Noul thresholds, including a Noul near 0 or 1 that does not escalate.
  - A confident answer is never re-asked.
  - When the time left is below the target's expected latency, escalation is skipped, the local answer is returned, and the reason is recorded.
  - A failing target leaves the local answer in place, with the reason recorded.
  - An escalated answer replaces the local one even when it is less confident, and the path records both confidences.
  - The path that answered each question is on the response, on the span, in the counters, and in the recorder's record.
  - No state appears in a log, a trace or an exception message.
- [ ] `UseFallback` moves through a chain of keyed clients on failure, composes with `UseRetries` and DI, skips a step that does not fit the time left, and records which client answered. The documented composition order with `UseEscalation` is pinned by a test.
- [ ] A ticket-triage sample runs local first with escalation on low confidence, and a committed report shows the split by path, with latency and cost. Each hosted run needs the maintainer's approval.
- [ ] A guide on the docs site teaches confidence routing as a framework feature: thresholds sized to the cost of being wrong, the time check, the fallback chain, composition order, privacy of targets, and chat-model escalation.
- [ ] A dotnet/extensions proposal draft for the decision abstraction is in the repository, with no private details, and the maintainer has approved it.
- [ ] The new API ships in a minor release through release-please, and ApiCompat is clean against the previous release.

**Out of scope:**
- Posting the dotnet/extensions discussion: the maintainer posts the approved draft, or tells me to.
- Daedalus adopting escalation: Daedalus's own work.
- A measured running median of target latency: expected latency is configured only.
- A live OpenAI step: #161 still has no OpenAI account.

## Phases
1. Phase 10.1 — Escalation middleware [active]
2. Phase 10.2 — Provider fallback chain [pending]
3. Phase 10.3 — Ticket-triage sample [pending]
4. Phase 10.4 — Confidence routing as a framework feature [pending]

## Audit History
| Date | Verdict | Gaps |
|---|---|---|
