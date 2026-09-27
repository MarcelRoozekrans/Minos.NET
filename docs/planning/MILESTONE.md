# Milestone 1: Foundation & core client

**Status:** complete
**Completed:** 2026-09-27
**Started:** 2026-09-24

## Goal
Deliver a working, Native AOT-clean `JevClient` for TypeSafe's Jev API covering `POST /v1/systemone` (Noul, Choice, Score) and `GET /v1/models`, built on the ZeroAlloc stack (Rest, Serialisation, Results, Resilience) and targeting net10.0, plus the core of a `[JevQuestions]` source generator that turns C# types into question JSON and typed answers. The repository follows the AdoNet.Async standards, and CI gates every change on tests (unit, WireMock.Net component, key-gated live smoke), an AOT smoke publish with allocation budgets, and a BenchmarkDotNet smoke gate. release-please owns releases; NuGet publishing is deferred until the maintainer declares the package mature (#29).

## Definition of Done
- [x] All planned phases complete
- [x] All tests passing (unit, WireMock.Net component, live smoke when key present)
- [x] `JevClient` calls both endpoints and returns `Result<T, JevError>` for every documented error status
- [x] `[JevQuestions]` generator emits question JSON and parses answers, verified against the wire fixtures
- [x] 429/529 retried with backoff honoring `retry-after`
- [x] AOT smoke app publishes with zero IL2xxx/IL3xxx warnings in CI
- [x] BenchmarkDotNet smoke gate and `AllocationGate` budgets in CI
- [x] release-please wired; NuGet publishing deferred (#29)

## Phases
1. Phase 1.1 — Repo scaffolding [complete]
2. Phase 1.2 — Wire model [complete]
3. Phase 1.3 — Question generator core [complete]
4. Phase 1.4 — Transport and error model [complete]
5. Phase 1.5 — Rename to ZeroAlloc.Jev [complete]
6. Phase 1.6 — Resilience [complete]
7. Phase 1.7 — Test harness [complete]
8. Phase 1.8 — CI and release pipeline [complete]

## Audit History
| Date | Verdict | Gaps |
|---|---|---|
| 2026-09-27 | PASS | Notes: TypeSafe live suite not yet run; per-task and final reviews used instead of pre-push-review reports. See `docs/plans/2026-09-27-milestone-1-audit.md`. |
