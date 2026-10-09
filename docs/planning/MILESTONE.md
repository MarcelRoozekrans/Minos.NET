# Milestone 5: 1.0 hardening

**Status:** active
**Started:** 2026-10-04
**Design:** `docs/superpowers/specs/2026-10-04-milestone-5-design.md`

## Goal
Harden the client before 1.0. Its public API has been reviewed. It has been measured against a hand-written .NET client and against TypeSafe's official JS and Python SDKs. It has passed against TypeSafe's real API, and under Native AOT across its whole public surface.

Re-scoped on 2026-10-09: the 1.0 publish, api-compat and the version statements moved to Milestone 7, after Milestone 6 reshapes the library into a provider-neutral decision client (`docs/superpowers/specs/2026-10-09-roadmap-design.md`). Publishing `ZeroAlloc.Jev` 1.0.0 now would freeze a vendor-named API that Milestone 6 replaces.

## Definition of Done
- [ ] All planned phases complete.
- [ ] All tests pass: unit, generator, analyzer, DI, docs, integration, pack and samples, with every sample in replay mode. The TypeSafe live suite has passed at least once with a real key, as well as OpenRouter's.
- [ ] The public API has been reviewed:
  - every public type is sealed or deliberately open;
  - naming and nullability are reviewed;
  - XML docs are complete;
  - #67, #23, #24 and #25 are resolved;
  - ZeroAlloc.Telemetry 1.11.0 is adopted (#85);
  - #21 is closed.
- [ ] A published benchmark suite compares Jev with a hand-written raw `HttpClient` plus System.Text.Json client, and with TypeSafe's official JS and Python SDKs.
  - All four call one local mock server that serves recorded Jev responses.
  - The results are in `docs/performance.md` and on jev.zeroalloc.net, with each runtime and its version, and what each number measures.
- [ ] Native AOT and trim verification covers the whole public API, with zero IL2xxx and IL3xxx warnings.
- [ ] The `jev-latest` and `jev-preview` aliases are checked live.
- [ ] The allocation-measurement follow-ups are closed: #68, #73, #74 and #79.
- [ ] Every allocation budget is unchanged or tightened, never loosened.

## Phases
1. Phase 5.1 — Public API review [complete]
2. Phase 5.2 — Benchmark suite [complete]
3. Phase 5.3 — AOT, trim and measurement verification [complete]
4. Phase 5.4 — Live and alias verification [complete]

## Audit History
| Date | Verdict | Gaps |
|---|---|---|
