# Session State — 2026-09-27

**Date:** 2026-09-27

## Current Position
- **Milestone:** 1 — Foundation & core client (6 of 8 phases complete)
- **Phase:** 1.7 — Test harness (active; spec and plan written, executing on branch `phase/1.7-test-harness`)
- **Last completed task:** Phase 1.6 — Resilience, merged as PR #39 on 2026-09-27; closes #18.
- **Next task:** Execute the phase 1.7 plan subagent-driven; push and open the pull request after the maintainer's go-ahead. `OPENROUTER_API_KEY` is set locally, so only deliberate runs may touch the live project.

## What Phase 1.6 shipped
- `JevClient` retries 429, 503/529, other 5xx, 408, network failures and client time-outs with exponential backoff through ZeroAlloc.Resilience 3.2.0's `[Retry]` on the internal `IJevApi`, honouring `retry-after-ms` and `Retry-After` capped by `MaxRetryDelay`; exhausted retries return the last `JevError`.
- Public options `MaxRetries` (2), `InitialBackoff` (500 ms), `MaxRetryDelay` (30 s), `Jitter` (true); `Timeout` is per attempt.
- `RetryAfterHeader` reads all RFC 9110 date forms, pivots RFC 850 years on the current date, clamps overflow and rejects non-finite `retry-after-ms`.
- Tracked: ZeroAlloc-Net/ZeroAlloc.Resilience#195 (the `JevClient.ThrowDeclined` unwrap is a workaround until it ships), ZeroAlloc-Net/ZeroAlloc.Resilience#197 (unused package dependencies), #38 (HLQ005 pragmas), #40 (`X-TypeSafe-Retry-Count` parity).
- Lesson: a merge commit carries the pull request title in its body, so a conventional PR title adds a second changelog entry in release PR #33. Give phase PRs a plain title, not a conventional one.

## What Phase 1.5 shipped
- Repository https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev, public; `main` protected by ruleset "Main" (required checks `build`, `aot-smoke`; pull requests with 1 approval; admin bypass for merges, since the only maintainer cannot approve their own pull request).
- Release PR #33 (`chore(main): release 0.1.0`) is open and stays open: releases and NuGet publishing wait until the maintainer declares the package mature. There is no publish job; the org's `NUGET_API_KEY` is visible to this repository, so any future publish workflow must be a deliberate step.
- Renovate is on the org configuration. It merged two dependency PRs into `main` before the ruleset existed; the deliberate `Microsoft.CodeAnalysis.CSharp` 5.0.0 pin is now guarded with the exact NuGet range `[5.0.0]` (a bare `5.0.0` means 5.0.0 or higher, which is why #34 appeared).

## What Phase 1.4 shipped
- Public `IJevClient` / `JevClient` (four constructors without optional parameters), `JevClientOptions` (`set` accessors), `JevProvider`, `JevError` / `JevErrorKind`; internal `IJevApi` over ZeroAlloc.Rest 2.1.0 with `[ErrorMapper(typeof(JevErrorMapper))]`, `JevClientSettings`, `RetryAfterHeader`.
- OpenRouter: `SystemOneResponse.Id` / `Provider`, `JevUsage.Cost`; `ListModelsAsync` returns `Unsupported` without a request; `TYPESAFE_BASE_URL` applies to TypeSafe only.
- Upstream issues filed: ZeroAlloc-Net/ZeroAlloc.Rest#335 (HLQ001 in generated code, which `ZeroAlloc.Jev.csproj` suppresses project-wide until it ships) and #336 (transitive dependency footprint).
- Follow-ups for later phases are listed at the end of the phase 1.4 plan.

## What Phase 1.3 shipped
- Runtime types in `ZeroAlloc.Jev`: `Noul`, `Choice<T>`, `Score<T>`, `ProbabilityMap<T>` (internal constructor), `JevOptionSet<T>`, `IJevQuestionSet<TSelf>`, the six question attributes, and `JevAnswerReader`, a public helper hidden from IntelliSense that the generated code calls.
- Generator `src/ZeroAlloc.Jev.Generator` (netstandard2.0, Roslyn 5.0.0): `QuestionsUtf8` as a pure-ASCII u8 literal, a `Parse` that dispatches to `JevAnswerReader`, and one private option set per Choice/Score question. It reports JEV101–JEV106.
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
- Decided 2026-09-27 (phase 1.6): retry 429, 503/529, other 5xx, 408, network failures and time-outs; defaults mirror the official TypeSafe Python SDK.
- Phase 1.3 and 1.4 follow-ups are tracked as issues labelled `follow-up` on ZeroAlloc-Net/ZeroAlloc.Jev.

## Blockers
- None for Milestone 1.
- Unknown until a live API call: whether TypeSafe sends `Retry-After`, and the schema of the 422 error body.

## Recommended Next Step
Run `start-next-phase` for Phase 1.7 — Test harness on a new branch. Leave release PR #33 open until the package is declared mature.
