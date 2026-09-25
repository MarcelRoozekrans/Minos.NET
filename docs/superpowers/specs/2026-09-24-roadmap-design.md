# Roadmap Design — Jev.Net

**Date:** 2026-09-24
**Author:** Claude (brainstormed with Marcel Roozekrans)
**Stage:** roadmap (whole project)

## Goal

Jev.Net is a community .NET SDK for TypeSafe AI's Jev, the first "System One" model: instead of generating text, Jev evaluates a `state` against typed questions (Noul, Choice, Score) and returns calibrated probabilities and confidence. TypeSafe ships official Python and JavaScript SDKs but nothing for .NET. Jev.Net fills that gap with a client built on the ZeroAlloc.* ecosystem — source-generated, Native AOT-clean, allocation-budgeted and benchmarked — that turns Jev's questions and answers into strongly typed C#. Success at project level: a stable 1.0 on NuGet that a .NET developer can adopt in minutes, that is measurably cheap on the hot path, and that tracks the TypeSafe API as it evolves.

## Target Users / Stakeholders

- **Primary: .NET developers building decision logic on Jev** — need a typed, idiomatic client (`Choice<TEnum>`, typed answers, `Result<T, E>` error handling) instead of hand-rolled HttpClient + JSON.
- **Performance-sensitive / AOT users** (serverless, edge, high-throughput pipelines) — need Native AOT publish without warnings, zero-reflection code paths, and published allocation/latency numbers.
- **ZeroAlloc ecosystem users** — expect the same conventions (Inject, Resilience, Telemetry, Results) as the rest of ZeroAlloc-Net.
- **Maintainer** — needs the repo to follow the AdoNet.Async standards (analyzers, release-please, GitVersion alphas, Renovate, Docusaurus) so it is cheap to maintain.
- **Out of scope:** TypeSafe itself (this is an unofficial client — no impersonation of official branding); .NET 8 / .NET Framework / netstandard consumers (net10.0 only, forced by ZeroAlloc.Rest); non-Jev LLM providers.

## Top-Level Success Criteria

- `Jev.Net` and `Jev.Net.Extensions.DependencyInjection` 1.0.0 are published on nuget.org via release-please, targeting net10.0.
- Every public API call of `/v1/systemone` and `/v1/models` (all three question types, structured instructions/criteria, all documented error statuses) is covered by WireMock.Net component tests, and a live smoke suite passes against the real API in CI.
- An AOT smoke app using the full public surface publishes with `PublishAot=true` and zero IL2xxx/IL3xxx warnings; the CI gate enforces it.
- Published BenchmarkDotNet results and `AllocationGate` budgets show client-side overhead (request building + response parsing) below a raw HttpClient + System.Text.Json baseline.
- A Docusaurus site on GitHub Pages documents getting started, every question type, the DI integration, and at least three Jev patterns ported to C#.

## Milestones

### Milestone 1: Foundation & core client

**Goal:** A working, AOT-clean `JevClient` covering both endpoints, with CI gates (tests, AOT smoke, benchmarks) and alpha packages flowing to NuGet.

**Phases:**

1. Repo scaffolding per AdoNet.Async standards (`.slnx`, `Directory.Build.props`, analyzers, `.editorconfig`, commitlint, Renovate, logo placeholder) — `Surface: Infra`
2. Wire model for the HTTP API, serialised via STJ source generation / ZeroAlloc.Serialisation — `Surface: Backend`
3. Question generator core: `[JevQuestions]` source generator emitting question JSON and a typed answer parser (added 2026-09-25, see `2026-09-25-question-generator-roadmap-design.md`) — `Surface: Backend`
4. Transport: ZeroAlloc.Rest interface for `/v1/systemone` and `/v1/models`, Bearer auth, `TYPESAFE_API_KEY` — `Surface: Backend`
5. Error model: `Result<T, JevError>` covering 401/422/429/529, network failures and timeouts — `Surface: Backend`
6. Resilience: ZeroAlloc.Resilience retry on 429/529 honoring `retry-after` — `Surface: Backend`
7. Test harness: unit tests, WireMock.Net component tests, key-gated live smoke suite — `Surface: Infra`
8. CI: build/test, AOT smoke with `AllocationGate`, BenchmarkDotNet baseline, release-please, GitVersion alpha pushes — `Surface: Infra`

### Milestone 2: Typed .NET API

**Goal:** Questions and answers become strongly typed, idiomatic C# without reflection.

**Phases:**

> Reshaped 2026-09-25 around the `[JevQuestions]` source generator — see `2026-09-25-question-generator-roadmap-design.md`.

1. Typed evaluation: `EvaluateAsync<T>` → `Result<T, JevError>`, typed state via `State = typeof(...)`, raw overloads — `Surface: Backend`
2. Analyzers and code fixes: JEV001–JEV006 and a `[Criteria]`-stub code fix — `Surface: Backend`
3. Structured instructions/criteria (object/array, `Examples` / `NotFor`) and `state` helpers — `Surface: Backend`
4. Fluent question builders for runtime-defined questions, with runtime limit validation via ZeroAlloc.Validation — `Surface: Backend`
5. Allocation budgets and benchmarks for the typed layer — `Surface: Backend`

### Milestone 3: .NET integration

**Goal:** First-class generic-host, DI and observability experience.

**Phases:**

1. `Jev.Net.Extensions.DependencyInjection` package via ZeroAlloc.Inject + `IHttpClientFactory` — `Surface: Backend`
2. Options and configuration binding validated with ZeroAlloc.Validation.Options — `Surface: Backend`
3. Telemetry via ZeroAlloc.Telemetry: spans and metrics for tokens, latency, confidence — `Surface: Backend`
4. Source-generated `LoggerMessage` logging — `Surface: Backend`
5. Configurable resilience policies exposed through DI — `Surface: Backend`

### Milestone 4: Patterns & docs

**Goal:** Developers can learn and apply Jev's documented patterns in C#.

**Phases:**

1. Pattern helpers: confidence-gated routing, composite scoring, speculative fan-out — `Surface: Backend`
2. Runnable C# ports of cookbook samples (guardrails, intent routing, re-ranking) — `Surface: Docs`
3. Docusaurus site deployed to GitHub Pages — `Surface: Docs`
4. README, logo, and unofficial-client disclaimer — `Surface: Docs`

### Milestone 5: 1.0 hardening

**Goal:** A reviewed, frozen public API shipped as a stable 1.0.

**Phases:**

1. Public API review and sealing with PublicApiAnalyzers — `Surface: Refactor`
2. Full benchmark suite vs raw HttpClient + STJ baseline and the official JS SDK's overhead — `Surface: Backend`
3. Full-surface AOT/trim verification and model-alias (`jev-latest` / `jev-preview`) checks — `Surface: Infra`
4. Stable 1.0 release via release-please with versioned docs — `Surface: Infra`

## Dependencies and Ordering Rationale

- **M1 → M2:** the typed layer is built on the wire model and transport; typing an unproven transport would mean reworking both.
- **M2 → M3:** DI should register the typed client, not an interim untyped API that would then need a breaking change.
- **M3 → M4:** docs and cookbooks should show the host/DI setup users will actually use.
- **M4 → M5:** the API is frozen only after real usage in samples and docs has shaken out awkward shapes.
- **Cross-cutting:** AOT smoke and benchmarks are CI gates from M1 onward, not an M5 afterthought — M5 only broadens them.
- **External dependencies:** TypeSafe API access (limited early access; key held, stored as a CI secret); ZeroAlloc.* packages (net10.0, maintained in ZeroAlloc-Net); a GitHub remote (not yet created — needed before M1 phase 7 CI work lands).

## Risk Register

| Risk | Impact | Mitigation |
|---|---|---|
| TypeSafe API is weeks old and may change shape | Breaking wire changes | Live smoke suite in CI as early warning; wire model isolated from the typed public API |
| Rate limits "adjusting dynamically" | Flaky live tests, user-facing 429s | Retry honoring `retry-after`; live suite kept tiny and non-blocking on 429 |
| ZeroAlloc.Rest may not fit a dynamic `questions` map body | Transport rework | Spike in M1 phase 3; fallback to a custom body serialiser plugged into ZeroAlloc.Rest |
| `Result`-only error model unfamiliar vs official SDKs' exceptions | Adoption friction | Clear docs and samples; `Match`/`TryGet` ergonomics reviewed in M5 |
| net10.0-only excludes .NET 8 users | Smaller audience | Accepted trade-off; .NET 8 LTS ends Nov 2026 |
| Unofficial client mistaken for official | Brand/legal concerns | Package id `Jev.Net`, explicit disclaimer in README and package description |

## Open Questions

- GitHub repository owner (personal account vs ZeroAlloc-Net org) — decide before M1 phase 7; re-run `init-conventions` once the remote exists.
- Whether `Jev.Net` should eventually move under the ZeroAlloc-Net org / naming — revisit at M5.
