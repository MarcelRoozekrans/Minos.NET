# Milestone 11: Microsoft.Extensions.AI decision adapters

**Status:** active
**Started:** 2026-11-07
**Design:** `docs/superpowers/specs/2026-11-07-milestone-11-design.md`

## Goal
A .NET app can use Minos and the Microsoft.Extensions.AI decision abstraction together. A Minos `IDecisionClient`, with its pipeline of escalation, fallback, retries, caching and recording, can be handed to code that expects the upstream `IDecisionClient`. A provider that implements the upstream interface can sit behind Minos, so `UseEscalation` and `UseFallback` work over it. The upstream types are still experimental and unreleased, so the milestone builds the mapping and the adapters now, and publishes the package only when upstream ships a package that contains them.

## Definition of Done
- [ ] All planned phases are complete.
- [ ] All tests pass, including the AOT smoke and surface checks and the docs tests, with zero IL2 and IL3 warnings.
- [ ] A mapping document on the docs site names every difference between the Minos and the upstream decision types, and each one has a test that pins the adapter's behaviour: question kinds, state, answers and distributions, errors, options, `GetService`, cancellation and disposal.
- [ ] A Minos client wrapped as an upstream `IDecisionClient`, and an upstream client wrapped as a Minos one, both pass the Minos conformance checks that apply to them, and a round trip returns the same answers.
- [ ] `UseEscalation` and `UseFallback` run over an upstream-backed client in a test, and the path and answered-by fields survive the wrap.
- [ ] The adapter package builds against a local build of the upstream PR head in CI or a documented script, and stays unpacked, with a test that fails if it is packed before an upstream package exists.
- [ ] The maintainer has a short upstream-facing gap list (what upstream's Layer 1 lacks for a pipeline like Minos's), for use when posting the comment on issue 7764.
- [ ] If an upstream package with the decision types exists by the end of the milestone: the package ships as a beta in a minor release through release-please, with ApiCompat clean. Otherwise the publish is a tracked issue, and that is not a gap.

**Out of scope:**
- Posting the comment on dotnet/extensions issue 7764: the maintainer posts the approved draft.
- Adoption by Daedalus.NET.
- Upstream Layer 2 (function and routing composition).
- A Minos 2.0 that replaces Minos's types with upstream ones.

## Phases
1. Phase 11.1 — Mapping and gap analysis [pending]
2. Phase 11.2 — Adapters [pending]
3. Phase 11.3 — Publish when upstream ships [deferred]
   **Deferred:** no upstream package with the decision types on 2026-11-07; publishing waits on #352.

## Audit History
| Date | Verdict | Gaps |
|---|---|---|
