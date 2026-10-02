# Milestone 3: .NET integration

**Status:** complete
**Started:** 2026-09-30
**Completed:** 2026-10-01
**Design:** `docs/superpowers/specs/2026-09-30-milestone-3-design.md`

## Goal
Jev feels native in a .NET generic-host app. One call registers the client, including keyed clients per provider. Options bind from configuration and fail fast when they are invalid, and the same configuration carries the retry and timeout settings. Every evaluation emits structured logs through `ILogger`, plus spans and metrics for tokens, latency and confidence. It all stays Native AOT-clean and within CI-enforced allocation budgets. Logging and telemetry live in the core package; `ZeroAlloc.Jev.DependencyInjection` only wires them up.

## Definition of Done
- [x] All planned phases complete.
- [x] All tests passing: unit, generator, analyzer, integration and pack. Live smoke still runs when `JEV_LIVE=1` and a key are set.
- [x] `ZeroAlloc.Jev.DependencyInjection` registers the client:
  - `services.AddJevClient(...)` registers an `IJevClient` on `IHttpClientFactory` in one call.
  - `services.AddJevClient(name, ...)` registers keyed clients, each with its own options and `HttpClient`.
  - The pack tests assert the package's layout and dependencies.
- [x] `JevClientOptions` bind from `IConfiguration`, validated at startup with the core's own rules, so invalid values fail when the host starts. Retry and timeout settings are configurable this way.
- [x] `JevClient` logs through Microsoft's source-generated `[LoggerMessage]`:
  - It accepts an `ILoggerFactory` through new constructor overloads.
  - Logs never contain state, instructions, answers or the API key.
  - Without a logger, it logs nothing and allocates nothing for logging.
- [x] Spans and metrics come from ZeroAlloc.Telemetry:
  - They cover token usage, operation duration and answer confidence.
  - Names follow the OpenTelemetry GenAI conventions plus `jev.*`.
  - Jev's span nests the `ZeroAlloc.Rest` HTTP span; it does not duplicate it.
- [x] Every phase adds `AllocationGate` budgets and benchmarks for what it ships, and existing budgets hold with logging and telemetry disabled. The AOT smoke app exercises logging, telemetry and DI registration with zero IL2xxx/IL3xxx warnings.

## Phases
1. Phase 3.1 — Logging [complete]
2. Phase 3.2 — Telemetry [complete]
3. Phase 3.3 — DI package [complete]
4. Phase 3.4 — Options and configuration [complete]

## Audit History
| Date | Verdict | Gaps |
|---|---|---|
| 2026-10-01 | PASS | None blocking. Notes: TypeSafe live suite not run, ZeroAlloc.Telemetry#184 open upstream, no Phase 3.4 benchmark, no pre-push-review reports. See `docs/plans/2026-10-01-milestone-3-audit.md`. |
