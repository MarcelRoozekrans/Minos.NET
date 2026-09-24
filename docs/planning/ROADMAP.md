---
compress_memory: disabled
---

# Project Roadmap

> Source spec: `docs/superpowers/specs/2026-09-24-roadmap-design.md`

## Milestone 1: Foundation & core client [status: active]
**Goal:** A working, AOT-clean `JevClient` covering both endpoints, with CI gates (tests, AOT smoke, benchmarks) and alpha packages flowing to NuGet.
**Started:** 2026-09-24
**Definition of Done:**
- [ ] `JevClient` calls `POST /v1/systemone` (Noul, Choice, Score) and `GET /v1/models`, returning `Result<T, JevError>`
- [ ] 401/422/429/529, network failures and timeouts map to `JevError`; 429/529 retried honoring `retry-after`
- [ ] WireMock.Net component tests and key-gated live smoke suite pass
- [ ] AOT smoke app publishes with zero IL2xxx/IL3xxx warnings in CI
- [ ] BenchmarkDotNet baseline and `AllocationGate` budgets committed
- [ ] release-please and GitVersion alpha publishing wired

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

### Phase 1.3: Transport [status: pending]
**Goal:** ZeroAlloc.Rest interface for `/v1/systemone` and `/v1/models` with Bearer auth, `TYPESAFE_API_KEY` resolution, and a configurable base address (`TYPESAFE_BASE_URL`) supporting both TypeSafe direct and OpenRouter (`https://openrouter.ai/api`) as first-class providers.
**Surface:** Backend
**HelpWanted:** no
**Plan:** _to be written_

### Phase 1.4: Error model [status: pending]
**Goal:** `Result<T, JevError>` covering 401/422/429/529, network failures and timeouts.
**Surface:** Backend
**HelpWanted:** no
**Plan:** _to be written_

### Phase 1.5: Resilience [status: pending]
**Goal:** ZeroAlloc.Resilience retry with exponential backoff on 429/529 honoring `retry-after`.
**Surface:** Backend
**HelpWanted:** no
**Plan:** _to be written_

### Phase 1.6: Test harness [status: pending]
**Goal:** Unit tests, WireMock.Net component tests, and a live smoke suite skipped when no API key is present — covering both TypeSafe direct and OpenRouter.
**Surface:** Infra
**HelpWanted:** no
**Plan:** _to be written_

### Phase 1.7: CI and release pipeline [status: pending]
**Goal:** Build/test, AOT smoke with `AllocationGate`, BenchmarkDotNet baseline, release-please, and GitVersion alpha pushes to NuGet.
**Surface:** Infra
**HelpWanted:** no
**Plan:** _to be written_

## Milestone 2: Typed .NET API [status: pending]
**Goal:** Questions and answers become strongly typed, idiomatic C# without reflection.
**Definition of Done:**
- [ ] Fluent builders cover all three question types including structured instructions/criteria
- [ ] `Choice<TEnum>` / `Score<TEnum>` return typed answers with no runtime reflection
- [ ] Documented API limits validated client-side
- [ ] Typed layer stays within allocation budgets and AOT smoke stays clean

### Phase 2.1: Fluent question builders [status: pending]
**Goal:** Builders for Noul, Choice and Score plus a typed request map.
**Surface:** Backend
**HelpWanted:** no
**Plan:** _to be written_

### Phase 2.2: Enum-typed questions [status: pending]
**Goal:** `Choice<TEnum>` / `Score<TEnum>` with typed answers, reflection-free via generator or static abstract members.
**Surface:** Backend
**HelpWanted:** no
**Plan:** _to be written_

### Phase 2.3: Structured instructions and state [status: pending]
**Goal:** Object/array instructions and criteria plus `state` helpers.
**Surface:** Backend
**HelpWanted:** no
**Plan:** _to be written_

### Phase 2.4: Client-side validation [status: pending]
**Goal:** Enforce API limits (≤255 options, 2–10 levels, required fields) with ZeroAlloc.Validation.
**Surface:** Backend
**HelpWanted:** no
**Plan:** _to be written_

### Phase 2.5: Typed-layer performance [status: pending]
**Goal:** Allocation budgets and benchmarks for the builders and typed answer parsing.
**Surface:** Backend
**HelpWanted:** no
**Plan:** _to be written_

## Milestone 3: .NET integration [status: pending]
**Goal:** First-class generic-host, DI and observability experience.
**Definition of Done:**
- [ ] `Jev.Net.Extensions.DependencyInjection` registers the typed client via one call
- [ ] Options bind from configuration and fail fast on invalid values
- [ ] Spans and metrics (tokens, latency, confidence) emitted via ZeroAlloc.Telemetry
- [ ] Resilience policies configurable through DI

### Phase 3.1: DI package [status: pending]
**Goal:** `Jev.Net.Extensions.DependencyInjection` built on ZeroAlloc.Inject and `IHttpClientFactory`.
**Surface:** Backend
**HelpWanted:** no
**Plan:** _to be written_

### Phase 3.2: Options and configuration [status: pending]
**Goal:** Configuration binding validated with ZeroAlloc.Validation.Options.
**Surface:** Backend
**HelpWanted:** no
**Plan:** _to be written_

### Phase 3.3: Telemetry [status: pending]
**Goal:** ZeroAlloc.Telemetry spans and metrics for tokens, latency and confidence.
**Surface:** Backend
**HelpWanted:** no
**Plan:** _to be written_

### Phase 3.4: Logging [status: pending]
**Goal:** Source-generated `LoggerMessage` logging across the client.
**Surface:** Backend
**HelpWanted:** no
**Plan:** _to be written_

### Phase 3.5: Configurable resilience [status: pending]
**Goal:** Expose retry/timeout policies through DI configuration.
**Surface:** Backend
**HelpWanted:** no
**Plan:** _to be written_

## Milestone 4: Patterns & docs [status: pending]
**Goal:** Developers can learn and apply Jev's documented patterns in C#.
**Definition of Done:**
- [ ] Pattern helpers for confidence routing, composite scoring and fan-out shipped
- [ ] At least three cookbook samples run as C# projects
- [ ] Docusaurus site live on GitHub Pages
- [ ] README carries the unofficial-client disclaimer

### Phase 4.1: Pattern helpers [status: pending]
**Goal:** Confidence-gated routing, composite scoring and speculative fan-out helpers.
**Surface:** Backend
**HelpWanted:** no
**Plan:** _to be written_

### Phase 4.2: Cookbook samples [status: pending]
**Goal:** Runnable C# ports of guardrails, intent routing and re-ranking cookbooks.
**Surface:** Docs
**HelpWanted:** no
**Plan:** _to be written_

### Phase 4.3: Documentation site [status: pending]
**Goal:** Docusaurus site deployed to GitHub Pages.
**Surface:** Docs
**HelpWanted:** no
**Plan:** _to be written_

### Phase 4.4: README and branding [status: pending]
**Goal:** README, logo and unofficial-client disclaimer.
**Surface:** Docs
**HelpWanted:** no
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
