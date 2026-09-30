# Milestone 3: .NET integration

**Status:** active
**Started:** 2026-09-30
**Design:** `docs/superpowers/specs/2026-09-30-milestone-3-design.md`

## Goal
Jev feels native in a .NET generic-host app. One call registers the client, including keyed clients per provider. Options bind from configuration and fail fast when they are invalid, and the same configuration carries the retry and timeout settings. Every evaluation emits structured logs through `ILogger`, plus spans and metrics for tokens, latency and confidence. It all stays Native AOT-clean and within CI-enforced allocation budgets. Logging and telemetry live in the core package; `ZeroAlloc.Jev.DependencyInjection` only wires them up.

## Definition of Done
- [ ] All planned phases complete.
- [ ] All tests passing: unit, generator, analyzer, integration and pack. Live smoke still runs when `JEV_LIVE=1` and a key are set.
- [ ] `ZeroAlloc.Jev.DependencyInjection` registers the client:
  - `services.AddJevClient(...)` registers an `IJevClient` on `IHttpClientFactory` in one call.
  - `services.AddJevClient(name, ...)` registers keyed clients, each with its own options and `HttpClient`.
  - The pack tests assert the package's layout and dependencies.
- [ ] `JevClientOptions` bind from `IConfiguration`, validated with ZeroAlloc.Validation.Options so invalid values fail at startup. Retry and timeout settings are configurable this way.
- [ ] `JevClient` logs through Microsoft's source-generated `[LoggerMessage]`:
  - It accepts an `ILoggerFactory` through new constructor overloads.
  - Logs never contain state, instructions, answers or the API key.
  - Without a logger, it logs nothing and allocates nothing for logging.
- [ ] Spans and metrics come from ZeroAlloc.Telemetry:
  - They cover token usage, operation duration and answer confidence.
  - Names follow the OpenTelemetry GenAI conventions plus `jev.*`.
  - Jev's span nests the `ZeroAlloc.Rest` HTTP span; it does not duplicate it.
- [ ] Every phase adds `AllocationGate` budgets and benchmarks for what it ships, and existing budgets hold with logging and telemetry disabled. The AOT smoke app exercises logging, telemetry and DI registration with zero IL2xxx/IL3xxx warnings.

## Phases
1. Phase 3.1 — Logging [complete]
2. Phase 3.2 — Telemetry [pending]
3. Phase 3.3 — DI package [pending]
4. Phase 3.4 — Options and configuration [pending]

## Audit History
| Date | Verdict | Gaps |
|---|---|---|
