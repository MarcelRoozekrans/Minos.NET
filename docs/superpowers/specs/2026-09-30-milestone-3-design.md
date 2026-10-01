# Milestone 3 Design — .NET integration

**Date:** 2026-09-30
**Milestone:** 3
**Stage:** milestone (one milestone, multiple phases)

## Goal

Jev feels native in a .NET generic-host app:

- One call registers the client, including keyed clients per provider.
- Options bind from configuration and fail fast when they are invalid.
- Retry and timeout settings come from that same configuration.
- Every evaluation emits structured logs through `ILogger`, plus spans and metrics for tokens, latency and confidence.

All of it stays Native AOT-clean and within the CI-enforced allocation budgets. Logging and telemetry live in the core `ZeroAlloc.Jev` package, so callers who construct `JevClient` by hand get them too. `ZeroAlloc.Jev.DependencyInjection` only wires them up.

## Definition of Done

- [ ] All planned phases complete.
- [ ] All tests passing: unit, generator, analyzer, integration and pack. Live smoke still runs when `JEV_LIVE=1` and a key are set.
- [ ] `ZeroAlloc.Jev.DependencyInjection` registers the client.
  - `services.AddJevClient(...)` registers an `IJevClient` on `IHttpClientFactory` in one call.
  - `services.AddJevClient(name, ...)` registers keyed clients, each with its own options and `HttpClient`, resolved with `[FromKeyedServices]`.
  - The pack tests assert the package's layout and dependencies.
- [ ] `JevClientOptions` bind from `IConfiguration`.
  - They are validated at startup with the core's own rules, so invalid values fail when the host starts, not on the first call.
  - `MaxRetries`, `InitialBackoff`, `MaxRetryDelay`, `Jitter` and `Timeout` are configurable this way.
- [ ] `JevClient` logs through Microsoft's source-generated `[LoggerMessage]` methods.
  - It accepts an `ILoggerFactory` through new constructor overloads, none of them with optional parameters.
  - Logs never contain the state, instructions, answers or API key.
  - Without a logger, the client logs nothing and allocates nothing for logging.
- [ ] Spans and metrics come from ZeroAlloc.Telemetry.
  - They cover token usage, operation duration and answer confidence.
  - Names follow the OpenTelemetry GenAI semantic conventions where they fit, and `jev.*` names cover the rest.
  - Jev's span nests the `ZeroAlloc.Rest` HTTP span; it does not duplicate it.
- [ ] Every phase adds `AllocationGate` budgets and benchmarks for what it ships, and existing budgets hold with logging and telemetry disabled. The AOT smoke app exercises logging, telemetry and DI registration with zero IL2xxx/IL3xxx warnings.

Release PRs come from release-please, per `docs/planning/CONVENTIONS.md`. Completing the milestone creates no tag.

## Phases

1. **Phase 3.1: Logging** — `Surface: Backend`
   - **Goal:** Source-generated `[LoggerMessage]` logging across `JevClient`, plus constructor overloads that accept an `ILoggerFactory`.
2. **Phase 3.2: Telemetry** — `Surface: Backend`
   - **Goal:** ZeroAlloc.Telemetry spans and metrics in the core package for tokens, latency and confidence, named per the GenAI conventions plus `jev.*`.
3. **Phase 3.3: DI package** — `Surface: Backend`
   - **Goal:** `ZeroAlloc.Jev.DependencyInjection` with `AddJevClient(...)` and keyed clients over `IHttpClientFactory`, wiring logging and telemetry.
4. **Phase 3.4: Options and configuration** — `Surface: Backend`
   - **Goal:** `IConfiguration` binding validated at startup, with retry and timeout settings through configuration.

This order differs from the roadmap, which had DI → options → telemetry → logging → configurable resilience. Because logging and telemetry live in core, building them first lets the DI package be written once and never reopened.

The roadmap's Phase 3.5, configurable resilience, folds into 3.4. `JevClientOptions` already carries every retry and timeout setting, and `JevClient` builds its runtime retry policy from them. Binding the options from configuration is therefore what makes resilience configurable through DI.

Custom retry policies supplied through DI are left out until someone asks for them.

## Dependencies on Prior Milestones

- **Milestone 2's typed API.** Generated sets and built `JevQuestionSet`s are both evaluated through `IJevClient`. The interface grows only through default interface methods (#22), so existing implementations keep compiling.
- **Milestone 1's transport.** `JevClient` sits over the internal ZeroAlloc.Rest `IJevApi`, which already emits a `ZeroAlloc.Rest` span and meter, with retries from ZeroAlloc.Resilience.
- **External, all released on nuget.org on 2026-09-30:**

  | Package | Version |
  |---|---|
  | ZeroAlloc.Telemetry | 1.10.0 |

  ZeroAlloc.Validation.Options was dropped in Phase 3.4: startup validation reuses the core's own rules through `JevClientOptions.Validate()`, so there is one rule set.
  ZeroAlloc.Telemetry#142, metrics from a method's result, is closed.
  ZeroAlloc.Telemetry moved to 1.10.0, released on 2026-10-01, for Phase 3.2: 1.9.0 closed #168–#173, and 1.10.0 closed #181. See `docs/superpowers/specs/2026-10-01-phase-3.2-telemetry-design.md`.
  Phase 3.3 dropped ZeroAlloc.Inject and ZeroAlloc.Rest.DependencyInjection. `AddJevClient` is hand-written over `Microsoft.Extensions.Http` 10.0.0: Inject's attributes discover an app's own services, and Rest.DependencyInjection generates registrations for public Rest interfaces, while Jev's `IJevApi` is internal and built inside `JevClient`. See `docs/superpowers/specs/2026-10-01-phase-3.3-di-package-design.md`.
- **New core dependency:** `Microsoft.Extensions.Logging.Abstractions`.

## External Constraints

- **Upstream fixes.** The ZeroAlloc packages are developed in the same org, and upstream work goes through the org session (zeroalloc-ae). A gap there becomes an upstream issue, not a local workaround. The phase waits for the fix, as it did for ZeroAlloc.Validation#282.
- **GenAI conventions.** The OpenTelemetry GenAI semantic conventions are still marked "development", so their names may change before 1.0 (Milestone 5).
- **Live suite.** It needs a TypeSafe key and the `live-api` environment; neither exists yet. Live checks of the logs and telemetry are optional until they do.

## Risk Areas

| Risk | Impact | Mitigation |
|---|---|---|
| ZeroAlloc.Telemetry's attributes cannot express a GenAI name, a tag from the result, or a histogram of per-answer confidence | Wrong or missing signal names, or a hand-written `Meter` beside the generated one | Phase 3.2 spikes the attribute set first; a gap is filed upstream and waited for, not worked around |
| Jev's span duplicates or fails to nest the existing `ZeroAlloc.Rest` span | Double-counted latency, confusing traces | A test captures activities with an `ActivityListener` and asserts the parent/child structure |
| New `ILoggerFactory` constructor overloads clash with the existing four constructors or RS0026's overload rules | Source-breaking change for existing callers | Overloads only, no optional parameters, and an existing-constructor compile test |
| Each new ZeroAlloc dependency's generators flow to consumers transitively (NuGet/Home#6720) | Slower consumer builds; possible diagnostics in consumer code | The pack tests assert a consumer without the relevant attributes builds clean with no generated source, as for ZeroAlloc.Validation |
| Logging or telemetry costs time or allocations when no one listens | Regresses the Milestone 1–2 budgets | Guard on `IsEnabled` / listener presence; existing `AllocationGate` budgets must hold unchanged with logging and telemetry off |
| Logs or span tags leak state, instructions, answers or the API key | Privacy and security | Only metadata is logged (model, provider, question count, outcome, status, retry attempt), and a test asserts no payload text appears |
| Keyed registrations share an `HttpClient` or options instance by mistake | One provider's settings leak into another | Tests resolve two keyed clients and assert separate options and handlers |

## Open Questions

None. All decisions were made in the brainstorm on 2026-09-30:

- the package name `ZeroAlloc.Jev.DependencyInjection`;
- logging and telemetry in the core package;
- Phase 3.5 folded into 3.4;
- a default client plus keyed clients;
- GenAI conventions plus `jev.*` names;
- core observability built before DI;
- Microsoft's `[LoggerMessage]` for logging.
