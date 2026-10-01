# Milestone 3 Audit — .NET integration

**Date:** 2026-10-01
**Verdict:** PASS, with four notes

| Criterion | Result | Evidence |
|---|---|---|
| All planned phases complete | PASS | Phases 3.1–3.4 are complete in ROADMAP.md and MILESTONE.md. They merged as PR #69 (`b1709a2`), #72 (`b118b5c`), #75 (`d1c0f53`) and #76 (`12ef375`). |
| All tests passing: unit, generator, analyzer, integration and pack | PASS | `dotnet test -c Release` on `main` at `12ef375`: unit 749, DI 42, generator 226, analyzers 196, integration 26 and pack 12 pass. CI, Benchmarks, Release Please and the website trigger are green on `12ef375`. |
| Live smoke runs when `JEV_LIVE=1` and a key are set | PASS, with a note | The 8 live tests compile and skip without a key. The TypeSafe live suite has still not run, because no TypeSafe key is set locally or in the `live-api` environment. This carries over from Milestones 1 and 2. |
| `ZeroAlloc.Jev.DependencyInjection` registers the client: `AddJevClient(...)` on `IHttpClientFactory` in one call, keyed clients with their own options and `HttpClient`, pack tests on layout and dependencies | PASS | Phase 3.3 shipped the default and keyed overloads, and Phase 3.4 the `IConfiguration` overloads. The pack tests assert the layout and the exact dependencies: `ZeroAlloc.Jev`, `Microsoft.Extensions.Http` and `Microsoft.Extensions.Options.ConfigurationExtensions`, each with its floor. |
| `JevClientOptions` bind from `IConfiguration`, validated at startup with the core's own rules; retry and timeout settings configurable this way | PASS | Phase 3.4. `JevClientOptions.Validate()` runs the constructors' resolver. Every registration adds a validator per name plus `ValidateOnStart`. Binding is source-generated. Integration tests show a bound `MaxRetries` and `Timeout` on the wire, and the AOT smoke app binds every option. ZeroAlloc.Validation.Options was dropped from the DoD in favour of the core's own rules (Phase 3.4 decision). |
| `JevClient` logs through source-generated `[LoggerMessage]`; `ILoggerFactory` constructor overloads; no state, instructions, answers or key in logs; no logger means no logging cost | PASS | Phase 3.1: events 1001–1006, privacy tests, and an AOT discriminating check showing 0 B for logging when no logger is enabled. |
| Spans and metrics from ZeroAlloc.Telemetry cover tokens, duration and confidence; GenAI names plus `jev.*`; Jev's span nests the `ZeroAlloc.Rest` span | PASS, with a note | Phase 3.2. Upstream ZeroAlloc-Net/ZeroAlloc.Telemetry#184 is still open: on the exception path the span gets the exception message as its description and no `error.type`. The README documents this. |
| Every phase adds `AllocationGate` budgets and benchmarks for what it ships; existing budgets hold with logging and telemetry off; the AOT smoke app exercises logging, telemetry and DI with zero IL2xxx/IL3xxx warnings | PASS, with a note | 3.1: logged and unlogged gates, plus `ClientBenchmarks` logger rows. 3.2: listening gates, plus `TelemetryBenchmarks`. 3.3: relative and absolute DI gates, plus `DependencyInjectionBenchmarks`. 3.4: a relative gate for a bound client, at 4376 B per call. 3.4 adds no benchmark, because binding runs once at startup and its per-call path is the DI path `DependencyInjectionBenchmarks` already measures. Existing budgets are unchanged in every phase. The Phase 3.4 AOT publish had zero IL warnings. |
| Milestone-3 follow-ups #19 and #20 | PASS, to close | #19 (typed-client activation through `ActivatorUtilities`): `AddJevClient` registers the client with factory lambdas, so nothing activates `JevClient` by reflection. #20 (factory `HttpClient`s send no User-Agent): `JevClient.ConfigureHttpClient`, which `AddJevClient` applies, adds the User-Agent, and the DI tests assert it. A caller's own `HttpClient` can call the same method. Both issues are still open and should be closed with these notes. |
| Pre-push reviews on file | Note | There are no `docs/pre-push-review-*.md` reports. Every phase instead ran a review after each task, with fix rounds, and a final whole-branch review on the most capable model. |
| The release will tag correctly | Skipped | CONVENTIONS sets `Milestone completion tags a release: no`, because release-please owns releases. Release PR #63 (0.2.0) lists every Milestone 3 phase, including Phase 3.4's three `feat` entries and its `docs` entry. |

## Decisions recorded during the milestone
- **Factory request loggers removed (3.3).** `AddJevClient` calls `RemoveAllLoggers()` on Jev's named clients, because the factory's logging handlers cost 344 B per call even when nothing logs. `AddDefaultLogger()` restores them.
- **No `InternalsVisibleTo` for the DI package (3.3).** It would let a consumer who upgrades only the core break the DI package at run time. The DI package uses only public core API: `ConfigureHttpClient` in 3.3, `Validate()` in 3.4.
- **Startup validation reuses the core's rules (3.4).** Using ZeroAlloc.Validation.Options would have meant a second rule set, and attributes cannot express the environment-variable fallbacks.
- **Startup validation applies to every registration (3.4).** That holds when the app replaces `IJevClient`, and when it creates Jev's named `HttpClient` directly. Both cases are documented.
- **Dropped dependencies.** ZeroAlloc.Inject, ZeroAlloc.Rest.DependencyInjection and ZeroAlloc.Validation.Options were in the milestone design but were not needed.

## Follow-ups carried forward
- **Live suite.** Run the TypeSafe live suite once a key is available, and record whether TypeSafe sends `Retry-After` and what the 422 body looks like.
- **Performance baseline.** `docs/performance.md`'s Baseline still waits for a full **Benchmarks** workflow run.
- **Open maintainer decisions:**
  - #68: tighten three AOT budgets after a linux-x64 re-measurement.
  - ZeroAlloc.Telemetry#184's two API questions.
  - Whether to report the factory loggers' 344 B per call to dotnet/runtime.
  - Whether configuration errors should drop the core's ` (Parameter 'options')` suffix.
- **Open follow-up issues:**
  - #73: adopt TestHelpers' measuring API once TestHelpers#56 is released.
  - #74: route the hand-rolled zero-allocation tests through `AllocationGate`.
  - #67, #23, #24 and #25: Phase 5.1.
- **Unchanged.** #28 (api-compat) and #29 (NuGet publishing) still wait until the package is declared mature.
