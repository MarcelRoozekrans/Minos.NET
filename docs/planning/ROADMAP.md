---
compress_memory: disabled
---

# Project Roadmap

> Source spec: `docs/superpowers/specs/2026-09-24-roadmap-design.md`

## Milestone 1: Foundation & core client [status: complete]
**Goal:** A working, AOT-clean `JevClient` covering both endpoints, with CI gates (tests, AOT smoke, benchmarks); NuGet publishing is deferred until the maintainer declares the package mature (#29).
**Started:** 2026-09-24
**Completed:** 2026-09-27
**Definition of Done:**
- [x] `JevClient` calls `POST /v1/systemone` (Noul, Choice, Score) and `GET /v1/models`, returning `Result<T, JevError>`
- [x] `[JevQuestions]` generator emits question JSON and parses answers, verified against the wire fixtures
- [x] 401/422/429/529, network failures and timeouts map to `JevError`; 429/529 retried honoring `retry-after`
- [x] WireMock.Net component tests and key-gated live smoke suite pass
- [x] AOT smoke app publishes with zero IL2xxx/IL3xxx warnings in CI
- [x] BenchmarkDotNet smoke gate and `AllocationGate` budgets in CI
- [x] release-please wired; NuGet publishing deferred (#29)

### Phase 1.1: Repo scaffolding [status: complete]
**Goal:** Solution skeleton following AdoNet.Async standards: `.slnx`, `Directory.Build.props`, analyzers, `.editorconfig`, commitlint, Renovate, logo placeholder.
**Surface:** Infra
**HelpWanted:** no
**Plan:** `docs/superpowers/plans/2026-09-24-phase-1.1-repo-scaffolding.md`
**Completed:** 2026-09-24

### Phase 1.2: Wire model [status: complete]
**Goal:** Request/response types mirroring the HTTP API, serialised via System.Text.Json source generation / ZeroAlloc.Serialisation.
**Surface:** Backend
**HelpWanted:** no
**Plan:** `docs/superpowers/plans/2026-09-24-phase-1.2-wire-model.md`
**Completed:** 2026-09-24

### Phase 1.3: Question generator core [status: complete]
**Goal:** Incremental `[JevQuestions]` source generator with its attribute and runtime types (`[Noul]`, `[Choice]`, `[Score]`, `[Criteria]`, `[Level]`, `IJevQuestionSet<TSelf>`, typed `Noul` / `Choice<T>` / `Score<T>`), emitting `QuestionsUtf8` and a `Utf8JsonReader` answer parser, verified against the Phase 1.2 wire model and fixtures. No transport, `EvaluateAsync` or analyzers. Change spec: `docs/superpowers/specs/2026-09-25-question-generator-roadmap-design.md`.
**Surface:** Backend
**HelpWanted:** no
**Plan:** `docs/superpowers/plans/2026-09-26-phase-1.3-question-generator-core.md`
**Completed:** 2026-09-27

### Phase 1.4: Transport and error model [status: complete]
**Goal:** Public `JevClient` (`IJevClient`) calling `/v1/systemone` and `/v1/models` on TypeSafe or OpenRouter through an internal ZeroAlloc.Rest 2.1.0 interface, returning `Result<T, JevError>` for every outcome via `[ErrorMapper]` — 401/422/429/529, other statuses, network failures, time-outs and unreadable responses. `JevClientOptions` with `JevProvider`, API key and base address from options or `TYPESAFE_API_KEY` / `OPENROUTER_API_KEY` / `TYPESAFE_BASE_URL`. Merges the former 1.4 Transport and 1.5 Error model (2026-09-27).
**Surface:** Backend
**HelpWanted:** no
**Plan:** `docs/superpowers/plans/2026-09-27-phase-1.4-transport-and-error-model.md`
**Completed:** 2026-09-27

### Phase 1.5: Rename to ZeroAlloc.Jev [status: complete]
**Goal:** Rename the package, root namespace, projects and generator references from `Jev.Net` to `ZeroAlloc.Jev` — the NuGet id `Jev.Net` is owned by another publisher (JohnCampionJr, since 2026-09-20). Covers namespaces, project and folder names, the solution, generator metadata names and emitted `global::` references, snapshots, PublicAPI files, package metadata and docs; the package is published from the ZeroAlloc.NET NuGet account with the repo in the ZeroAlloc-Net GitHub org. The result must fit the ZeroAlloc-Net org: conventions surveyed from the sibling repos (ZeroAlloc.Rest, .Results, .Resilience) for naming, layout, build props, package metadata, docs and CI shape; and Native AOT: a smoke app publishes with `PublishAot` and zero IL2xxx/IL3xxx warnings, with the `aot-smoke` CI job landing in this phase and becoming a required check.
**Surface:** Refactor
**HelpWanted:** no
**Plan:** `docs/superpowers/plans/2026-09-27-phase-1.5-rename-to-zeroalloc-jev.md`
**Completed:** 2026-09-27

### Phase 1.6: Resilience [status: complete]
**Goal:** ZeroAlloc.Resilience retry with exponential backoff on 429, 503/529, other 5xx, 408, network failures and time-outs, honouring `Retry-After` and `retry-after-ms`; `MaxRetries`, `InitialBackoff`, `MaxRetryDelay` and `Jitter` on `JevClientOptions`, defaulting to the official TypeSafe SDK's 2 retries, 500 ms, 30 s and jitter. Completes `RetryAfterHeader` (#18).
**Surface:** Backend
**HelpWanted:** no
**Spec:** `docs/superpowers/specs/2026-09-27-phase-1.6-resilience-design.md`
**Plan:** `docs/superpowers/plans/2026-09-27-phase-1.6-resilience.md`
**Completed:** 2026-09-27

### Phase 1.7: Test harness [status: complete]
**Goal:** Unit tests, WireMock.Net component tests, and a live smoke suite skipped when no API key is present — covering both TypeSafe direct and OpenRouter.
**Surface:** Infra
**HelpWanted:** no
**Spec:** `docs/superpowers/specs/2026-09-27-phase-1.7-test-harness-design.md`
**Plan:** `docs/superpowers/plans/2026-09-27-phase-1.7-test-harness.md`
**Completed:** 2026-09-27

### Phase 1.8: CI and release pipeline [status: complete]
**Goal:** Build/test, AOT smoke with `AllocationGate`, a BenchmarkDotNet smoke gate, release-please, a pack test verifying the packed nupkg carries the generator under `analyzers/dotnet/cs`, every trim and AOT warning promoted to an error, and a website trigger workflow that notifies the org site on `docs/` changes. Publishing to nuget.org and api-compat stay switched off until the maintainer declares the package mature; the repository itself is already public. Tracked: #28 (api-compat once a released baseline exists), #29 (NuGet publishing when mature); versioning is by release-please.
**Surface:** Infra
**HelpWanted:** no
**Spec:** `docs/superpowers/specs/2026-09-27-phase-1.8-ci-and-release-pipeline-design.md`
**Plan:** `docs/superpowers/plans/2026-09-27-phase-1.8-ci-and-release-pipeline.md`
**Completed:** 2026-09-27

## Milestone 2: Typed .NET API [status: complete]
**Goal:** Questions and answers become strongly typed, idiomatic C# with no reflection. They are declared as `[JevQuestions]` types or built fluently at runtime, evaluated through `EvaluateAsync<T>`, AOT-clean and within CI-enforced allocation budgets.
**Started:** 2026-09-27
**Completed:** 2026-09-30
**Design:** `docs/superpowers/specs/2026-09-27-milestone-2-design.md`
**Definition of Done:**
- [x] `[JevQuestions]` types evaluate end-to-end through `EvaluateAsync<T>` → `Result<T, JevError>`, with no reflection, plus raw `JsonElement` / string / UTF-8 overloads
- [x] All Jev diagnostics come from `ZeroAlloc.Jev.Analyzers` (JEV001–006, and JEV101–107 moved out of the generator); code fixes adding a missing `[Criteria]` or `[Level]` in `ZeroAlloc.Jev.CodeFixes`; #4–#11 closed
- [x] Structured instructions and criteria work in attributes and builders
- [x] Fluent builders cover all three question types, with runtime API-limit validation via ZeroAlloc.Validation
- [x] Every phase adds `AllocationGate` budgets and benchmarks for what it ships; the AOT smoke app stays clean; #12, #13 and #22 closed

### Phase 2.1: Typed evaluation [status: complete]
**Goal:** `EvaluateAsync<T>` returning `Result<T, JevError>`, with:
- typed state via `[JevQuestions(State = typeof(...))]` and a caller-supplied `JsonTypeInfo`;
- raw `JsonElement` / string / UTF-8 overloads;
- a non-breaking `IJevClient` shape (#22);
- the record-equality fix (#12) and the ProbabilityMap boxing fix (#13), with budgets and benchmarks.

**Surface:** Backend
**HelpWanted:** no
**Spec:** `docs/superpowers/specs/2026-09-27-phase-2.1-typed-evaluation-design.md`
**Plan:** `docs/superpowers/plans/2026-09-27-phase-2.1-typed-evaluation.md`
**Completed:** 2026-09-27

### Phase 2.2: Analyzers and code fixes [status: complete]
**Goal:** `ZeroAlloc.Jev.Analyzers` hosts JEV001–006 (empty enums, empty text, state-member references, option and level guidance, missing criteria) and the JEV101–107 checks, which move out of the generator (#4; #5–#11 fixed along the way). `ZeroAlloc.Jev.CodeFixes` adds code fixes for a missing `[Criteria]` and a missing `[Level]`. Both are packed under `analyzers/dotnet/cs` and asserted by the pack tests.
**Surface:** Backend
**HelpWanted:** no
**Spec:** `docs/superpowers/specs/2026-09-27-phase-2.2-analyzers-and-code-fixes-design.md`
**Plan:** `docs/superpowers/plans/2026-09-28-phase-2.2-analyzers-and-code-fixes.md`
**Completed:** 2026-09-28

### Phase 2.3: Structured instructions and criteria [status: complete]
**Goal:** `Examples` / `NotFor` in attributes, sent as a criterion object, which is a Jev.Net convention and not an API field; object and array instructions and criteria; and `state` helpers. This spans the attributes, the generator and the analyzers, with budgets for the new paths.
**Surface:** Backend
**HelpWanted:** no
**Spec:** `docs/superpowers/specs/2026-09-28-phase-2.3-structured-instructions-and-criteria-design.md`
**Plan:** `docs/superpowers/plans/2026-09-28-phase-2.3-structured-instructions-and-criteria.md`
**Completed:** 2026-09-28

### Phase 2.4: Fluent question builders [status: complete]
**Goal:** Builders for runtime-defined Noul, Choice and Score questions that share the typed answer types, with:
- runtime API-limit validation via ZeroAlloc.Validation, using limits shared with the analyzers;
- allocation budgets and benchmarks for the builders.

**Surface:** Backend
**HelpWanted:** no
**Spec:** `docs/superpowers/specs/2026-09-28-phase-2.4-fluent-question-builders-design.md`
**Plan:** `docs/superpowers/plans/2026-09-28-phase-2.4-fluent-question-builders.md`
**Completed:** 2026-09-30

## Milestone 3: .NET integration [status: complete]
**Goal:** Jev feels native in a .NET generic-host app: one-call registration, including keyed clients per provider; options bound from configuration and failing fast, carrying the retry and timeout settings; and structured logs, spans and metrics for tokens, latency and confidence. Logging and telemetry live in the core package, and the DI package wires them up. AOT-clean and within CI-enforced allocation budgets.
**Started:** 2026-09-30
**Completed:** 2026-10-01
**Design:** `docs/superpowers/specs/2026-09-30-milestone-3-design.md`
**Definition of Done:**
- [x] `ZeroAlloc.Jev.DependencyInjection` registers the client in one call, and keyed clients with their own options and `HttpClient`, over `IHttpClientFactory`
- [x] `JevClientOptions` bind from `IConfiguration` and fail fast on invalid values, validated at startup; retry and timeout settings are configurable this way
- [x] Source-generated `[LoggerMessage]` logging through `ILogger`, with no payload or key in logs and no cost without a logger
- [x] Spans and metrics for tokens, latency and confidence via ZeroAlloc.Telemetry, named per the GenAI conventions plus `jev.*`, nesting the `ZeroAlloc.Rest` span
- [x] Every phase adds `AllocationGate` budgets and benchmarks for what it ships, and existing budgets hold; the AOT smoke app exercises logging, telemetry and DI

### Phase 3.1: Logging [status: complete]
**Goal:** Source-generated `[LoggerMessage]` logging across `JevClient`, with constructor overloads that accept an `ILoggerFactory`.
**Surface:** Backend
**HelpWanted:** no
**Spec:** `docs/superpowers/specs/2026-09-30-phase-3.1-logging-design.md`
**Plan:** `docs/superpowers/plans/2026-09-30-phase-3.1-logging.md`
**Completed:** 2026-09-30

### Phase 3.2: Telemetry [status: complete]
**Goal:** ZeroAlloc.Telemetry spans and metrics in the core package for tokens, latency and confidence, named per the GenAI conventions plus `jev.*`.
**Surface:** Backend
**HelpWanted:** no
**Spec:** `docs/superpowers/specs/2026-10-01-phase-3.2-telemetry-design.md`
**Plan:** `docs/superpowers/plans/2026-10-01-phase-3.2-telemetry.md`
**Completed:** 2026-10-01

### Phase 3.3: DI package [status: complete]
**Goal:** `ZeroAlloc.Jev.DependencyInjection` with `AddJevClient(...)` and keyed clients over `IHttpClientFactory`, wiring logging and telemetry.
**Surface:** Backend
**HelpWanted:** no
**Spec:** `docs/superpowers/specs/2026-10-01-phase-3.3-di-package-design.md`
**Plan:** `docs/superpowers/plans/2026-10-01-phase-3.3-di-package.md`
**Completed:** 2026-10-01

### Phase 3.4: Options and configuration [status: complete]
**Goal:** `IConfiguration` binding validated at startup with the core's own rules, with retry and timeout settings through configuration. Absorbs the roadmap's former Phase 3.5, configurable resilience (2026-09-30).
**Surface:** Backend
**HelpWanted:** no
**Spec:** `docs/superpowers/specs/2026-10-01-phase-3.4-options-configuration-design.md`
**Plan:** `docs/superpowers/plans/2026-10-01-phase-3.4-options-configuration.md`
**Completed:** 2026-10-01

## Milestone 4: Patterns & docs [status: active]
**Goal:** A C# developer can learn Jev from its own docs site and apply TypeSafe's documented patterns in idiomatic C#, with thin allocation-free helpers, original runnable samples kept honest by replayed recordings, and a user guide served at jev.zeroalloc.net.
**Started:** 2026-10-02
**Design:** `docs/superpowers/specs/2026-10-02-milestone-4-design.md`
**Definition of Done:**
- [ ] Pattern helpers ship in the core: a normalized Score value and a confidence-tier gate, allocation-free and exercised under Native AOT
- [ ] Guides for fan-out, confidence routing, composite scoring and intent routing, with C# snippets that compile in CI
- [ ] Original guardrails, intent-routing and re-ranking samples run as C# projects, live or from replayed recordings, and CI checks their decisions in replay mode
- [ ] The user guide in `docs/` is served at jev.zeroalloc.net through ZeroAlloc-Net/.website
- [ ] README carries the unofficial-client disclaimer and the logo, and links the site

### Phase 4.1: Pattern helpers and guides [status: complete]
**Goal:** Two allocation-free helpers, a normalized Score value and a confidence-tier gate with overridable defaults, plus guides for the four documented patterns with C# snippets that compile in CI.
**Surface:** Backend
**HelpWanted:** no
**Spec:** `docs/superpowers/specs/2026-10-02-phase-4.1-pattern-helpers-design.md`
**Plan:** `docs/superpowers/plans/2026-10-02-phase-4.1-pattern-helpers.md`
**Completed:** 2026-10-02

### Phase 4.2: Cookbook samples [status: complete]
**Goal:** Original guardrails, intent-routing and re-ranking samples that run live or in replay mode from recorded OpenRouter answers, with CI checking their decisions in replay mode.
**Surface:** Docs
**HelpWanted:** no
**Spec:** `docs/superpowers/specs/2026-10-02-phase-4.2-cookbook-samples-design.md`
**Plan:** `docs/superpowers/plans/2026-10-02-phase-4.2-cookbook-samples.md`
**Completed:** 2026-10-02

### Phase 4.3: User guide [status: complete]
**Goal:** The user guide in `docs/`, in the org layout: getting started, every question type, typed evaluation and builders, DI and configuration, logging and telemetry, Native AOT, patterns and samples. Closes #16.
**Surface:** Docs
**HelpWanted:** no
**Spec:** `docs/superpowers/specs/2026-10-02-phase-4.3-user-guide-design.md`
**Plan:** `docs/superpowers/plans/2026-10-02-phase-4.3-user-guide.md`
**Completed:** 2026-10-02

### Phase 4.4: Docs site, logo and README [status: active]
**Goal:** Register the repository in ZeroAlloc-Net/.website (`repos/jev` submodule and `apps/docs-jev` app) so `trigger-website.yml` publishes the guide to jev.zeroalloc.net; add a logo and slim the README to point at the site, keeping the disclaimer.
**Surface:** Docs
**HelpWanted:** no
**Spec:** `docs/superpowers/specs/2026-10-03-phase-4.4-docs-site-design.md`
**Plan:** _to be written_

## Milestone 5: 1.0 hardening [status: pending]
**Goal:** A reviewed, frozen public API shipped as a stable 1.0.
**Definition of Done:**
- [ ] Public API reviewed, sealed and tracked by PublicApiAnalyzers
- [ ] Full benchmark suite published
- [ ] Full-surface AOT/trim verification green
- [ ] 1.0.0 published to NuGet with versioned docs

### Phase 5.1: Public API review [status: pending]
**Goal:** Review and seal the public API, tracked with PublicApiAnalyzers.
**Surface:** Refactor
**HelpWanted:** no
**Plan:** _to be written_

### Phase 5.2: Benchmark suite [status: pending]
**Goal:** Full benchmarks vs a raw HttpClient + STJ baseline and the official JS SDK's overhead.
**Surface:** Backend
**HelpWanted:** no
**Plan:** _to be written_

### Phase 5.3: AOT and alias verification [status: pending]
**Goal:** Full-surface AOT/trim verification and `jev-latest` / `jev-preview` alias checks.
**Surface:** Infra
**HelpWanted:** no
**Plan:** _to be written_

### Phase 5.4: 1.0 release [status: pending]
**Goal:** Stable 1.0 via release-please with versioned docs.
**Surface:** Infra
**HelpWanted:** no
**Plan:** _to be written_
