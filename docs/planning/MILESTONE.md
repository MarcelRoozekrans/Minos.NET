# Milestone 5: 1.0 hardening

**Status:** active
**Started:** 2026-10-04
**Design:** `docs/superpowers/specs/2026-10-04-milestone-5-design.md`

## Goal
Ship ZeroAlloc.Jev 1.0.0 to NuGet. Before release, its public API has been reviewed and frozen. It has been measured against a hand-written .NET client and against TypeSafe's official JS and Python SDKs. It has passed against TypeSafe's real API, and under Native AOT across its whole public surface.

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
- [ ] `PublicAPI.Shipped.txt` describes 1.0.0, and api-compat (#28) checks every later change against the 1.0.0 package.
- [ ] A published benchmark suite compares Jev with a hand-written raw `HttpClient` plus System.Text.Json client, and with TypeSafe's official JS and Python SDKs.
  - All four call one local mock server that serves recorded Jev responses.
  - The results are in `docs/performance.md` and on jev.zeroalloc.net, with each runtime and its version, and what each number measures.
- [ ] Native AOT and trim verification covers the whole public API, with zero IL2xxx and IL3xxx warnings.
- [ ] The `jev-latest` and `jev-preview` aliases are checked live.
- [ ] The allocation-measurement follow-ups are closed: #68, #73, #74 and #79.
- [ ] 1.0.0 is published to NuGet through release-please and a publishing workflow (#29). The guide states the version it describes, and the README's Status line no longer says the package is unpublished.
- [ ] Every allocation budget is unchanged or tightened, never loosened.

## Phases
1. Phase 5.1 — Public API review [active]
2. Phase 5.2 — Benchmark suite [pending]
3. Phase 5.3 — Live, AOT and alias verification [pending]
4. Phase 5.4 — 1.0 release [pending]

## Audit History
| Date | Verdict | Gaps |
|---|---|---|
