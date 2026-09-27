# Session State — 2026-09-27

**Date:** 2026-09-27

## Current Position
- **Milestone:** 1 — Foundation & core client (4 of 8 phases complete)
- **Phase:** 1.5 — Rename to ZeroAlloc.Jev (pending; no design spec yet; Surface: Refactor)
- **Last completed task:** Phase 1.4 — Transport and error model: all 4 tasks of `docs/superpowers/plans/2026-09-27-phase-1.4-transport-and-error-model.md` plus the final-review fix wave. 180 tests in `Jev.Net.Tests` and 55 in `Jev.Net.Generators.Tests` pass, Release build 0 warnings, and the final review published a Native AOT console app over `JevClient` with zero IL warnings.
- **Next task:** Phase 1.5 brainstorm — survey the sibling ZeroAlloc-Net repos, then refactor-analysis before the plan.

## What Phase 1.4 shipped
- Public `IJevClient` / `JevClient` (four constructors without optional parameters), `JevClientOptions` (`set` accessors), `JevProvider`, `JevError` / `JevErrorKind`; internal `IJevApi` over ZeroAlloc.Rest 2.1.0 with `[ErrorMapper(typeof(JevErrorMapper))]`, `JevClientSettings`, `RetryAfterHeader`.
- OpenRouter: `SystemOneResponse.Id` / `Provider`, `JevUsage.Cost`; `ListModelsAsync` returns `Unsupported` without a request; `TYPESAFE_BASE_URL` applies to TypeSafe only.
- Upstream issues filed: ZeroAlloc-Net/ZeroAlloc.Rest#335 (HLQ001 in generated code, which `Jev.Net.csproj` suppresses project-wide until it ships) and #336 (transitive dependency footprint).
- Follow-ups for later phases are listed at the end of the phase 1.4 plan.

## What Phase 1.3 shipped
- Runtime types in `Jev.Net`: `Noul`, `Choice<T>`, `Score<T>`, `ProbabilityMap<T>` (internal constructor), `JevOptionSet<T>`, `IJevQuestionSet<TSelf>`, the six question attributes, and `JevAnswerReader`, a public helper hidden from IntelliSense that the generated code calls.
- Generator `src/Jev.Net.Generators` (netstandard2.0, Roslyn 5.0.0): `QuestionsUtf8` as a pure-ASCII u8 literal, a `Parse` that dispatches to `JevAnswerReader`, and one private option set per Choice/Score question. It reports JEV101–JEV106.
- Decisions taken without the user while they were away. They are listed with what each costs if wrong in the session's final message, and recorded in the plan's "Follow-ups from the final review":
  - Work went directly on `main`, as the trunk conventions allow.
  - `LocationInfo` holds the `SyntaxTree`, so diagnostics are reported at in-source locations.
  - Subagent commits carry their own `Co-Authored-By: Claude Sonnet 5` or `Claude Haiku 4.5` trailers.
  - The `ProbabilityMap<T>` constructor is internal.

## Upstream check (2026-09-26)
- ZeroAlloc.Rest 2.1.0 fixed #298 (error body) and #300 (custom error type); 2.0.0 fixed #299 and #301 (breaking; see `docs/migrating-to-v2.md`).
- ZeroAlloc.Resilience 3.2.0 fixed #142 (`RetryWhen`) and #143 (`DelayHint`).
- ZeroAlloc.Validation 2.0.0 is released (breaking; the generator is now bundled).
- Still open: Telemetry #142, needed only by Phase 3.3.
- Milestone 1 has no upstream blockers.

## Open Decisions
- Decided 2026-09-27: the repository is public at https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev (`origin`, `main` unprotected, no PRs required). NuGet publishing, including alpha pushes, waits until the maintainer declares the package mature — no NuGet secret is configured.
- Decided 2026-09-27: the NuGet id `Jev.Net` is taken by another publisher, so the project becomes `ZeroAlloc.Jev` (package and root namespace), published from the ZeroAlloc.NET NuGet account with the GitHub repo in the ZeroAlloc-Net org. The rename is Phase 1.5, right after 1.4; Resilience moved to 1.6, Test harness 1.7, CI and release 1.8.
- Decided 2026-09-27: former phases 1.4 Transport and 1.5 Error model merged into 1.4 "Transport and error model"; Resilience is now 1.5, Test harness 1.6, CI and release 1.7. OpenRouter model listing fails fast with `JevErrorKind.Unsupported`; configuration is a `JevProvider` enum plus `JevClientOptions`; `JevError` is one sealed type with a `Kind` enum.
- Phase 1.6: use ZeroAlloc.Resilience 3.2.0's `RetryWhen` and `DelayHint` for 429/529 and `Retry-After`.
- The remote now exists: file the phase 1.3 and 1.4 follow-ups as issues on ZeroAlloc-Net/ZeroAlloc.Jev. They are listed at the end of the phase 1.3 plan, grouped by the phase that owns them: 2.2 analyzers, 2.1/5.1 API, 1.7 packaging, 4.x README. The user rule is that every finding gets an issue, and until the remote exists they live only in the plan.

## Blockers
- None for Milestone 1.
- Unknown until a live API call: whether TypeSafe sends `Retry-After`, and the schema of the 422 error body.

## Recommended Next Step
Run `start-next-phase` for Phase 1.5 — Rename to ZeroAlloc.Jev. It has no design spec, so it routes to a brainstorm: survey ZeroAlloc.Rest, ZeroAlloc.Results and ZeroAlloc.Resilience for naming, layout, build props, package metadata, docs and CI; then the Surface: Refactor pre-plan hook runs `refactor-analysis` before `writing-plans`. The phase ends with a real `PublishAot` smoke publish (seed described in the phase 1.4 plan follow-ups).
