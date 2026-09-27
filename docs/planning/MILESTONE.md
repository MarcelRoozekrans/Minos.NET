# Milestone 1: Foundation & core client

**Status:** active
**Started:** 2026-09-24

## Goal
Deliver a working, Native AOT-clean `JevClient` for TypeSafe's Jev API covering `POST /v1/systemone` (Noul, Choice, Score) and `GET /v1/models`, built on the ZeroAlloc stack (Rest, Serialisation, Results, Resilience) and targeting net10.0, plus the core of a `[JevQuestions]` source generator that turns C# types into question JSON and typed answers. The repository follows the AdoNet.Async standards, and CI gates every change on tests (unit, WireMock.Net component, key-gated live smoke), an AOT smoke publish with allocation budgets, and a BenchmarkDotNet baseline. Alpha packages flow to NuGet from `main`; release-please owns stable releases.

## Definition of Done
- [ ] All planned phases complete
- [ ] All tests passing (unit, WireMock.Net component, live smoke when key present)
- [ ] `JevClient` calls both endpoints and returns `Result<T, JevError>` for every documented error status
- [ ] `[JevQuestions]` generator emits question JSON and parses answers, verified against the wire fixtures
- [ ] 429/529 retried with backoff honoring `retry-after`
- [ ] AOT smoke app publishes with zero IL2xxx/IL3xxx warnings in CI
- [ ] BenchmarkDotNet baseline and `AllocationGate` budgets committed
- [ ] release-please and GitVersion alpha publishing wired

## Phases
1. Phase 1.1 — Repo scaffolding [complete]
2. Phase 1.2 — Wire model [complete]
3. Phase 1.3 — Question generator core [complete]
4. Phase 1.4 — Transport and error model [active]
5. Phase 1.5 — Rename to ZeroAlloc.Jev [pending]
6. Phase 1.6 — Resilience [pending]
7. Phase 1.7 — Test harness [pending]
8. Phase 1.8 — CI and release pipeline [pending]

## Audit History
| Date | Verdict | Gaps |
|---|---|---|
