# Session State — 2026-09-30

**Date:** 2026-09-30

## Current Position
- **Milestone:** 2 — Typed .NET API, complete 2026-09-30 (audit PASS, `docs/plans/2026-09-30-milestone-2-audit.md`). Milestone 3 — .NET integration is next and pending.
- **Phase:** none active. Phase 3.1 (DI package) is the first of Milestone 3.
- **Last completed task:** Phase 2.4 — Fluent question builders, merged as PR #62 (`78874a2`); its follow-up #61 fix merged as PR #64 (`3a43c88`). Milestone 2 audit and completion on branch `chore/audit-milestone-2`.
- **Next task:** `new-milestone` for Milestone 3 — brainstorm its scope, then Phase 3.1.

## What Phase 2.4 shipped
- `JevQuestionSet.CreateBuilder()`: Noul, enum and keyed Choice, enum and keyed Score questions with configurators (closed after their callback) and handles. `Build()` returns `Result<JevQuestionSet, JevError>` after checking the analyzers' rules with ZeroAlloc.Validation 2.0.3; failures come back as the new `JevErrorKind.InvalidQuestions` with `JevError.Failures`, warnings on `JevQuestionSet.Warnings`. `QuestionsUtf8` is byte-identical to the generator's for the same set, which a differential test pins.
- `JevAnswers.Get(handle)` reads `Noul`, `Choice<T>`, `Score<T>`, `KeyedChoice` and `KeyedScore` without allocating; a handle from another builder, a `default` handle or one added after the build throws `ArgumentException`. `IJevClient.EvaluateAsync(JevQuestionSet questionSet, JevContent state)` with its CancellationToken overload are default interface methods; `JevClient` overrides them on its pooled path.
- Shared single sources: `JevLimits` (generator, analyzers, library), `Utf8Keys`, the generator's linked `SnakeCase.cs` and `DiagnosticIds.cs`, and `GeneratorJsonEncoder`, which escapes as the generator does.
- Budgets: Build 7296 B, evaluating a built set 4736 B, parsing a built set 256 B, `JevAnswers.Get` 0 B. A 20-question built parse runs at 1.26× the generated one, so no key map.
- Maintainer decisions: enum options are read from the enum's public fields in declaration order under `DynamicallyAccessedMembers` (exception to the no-reflection goal, since `Enum.GetName` names aliases by the alias in larger enums; verified under Native AOT). NuGet flows ZeroAlloc.Validation's analyzers to consumers transitively (NuGet/Home#6720); they stay inert. Score answers require `legend`, as the TypeSafe API does (#61, fixed in #64).
- Upstream: ZeroAlloc-Net/ZeroAlloc.Validation#282 (InclusiveBetween with When ignored its upper bound) was fixed in 2.0.3 by the zeroalloc-ae session, which owns upstream ZeroAlloc work.
- Lesson: phase PRs are squash-merged, so a plain PR title makes release-please drop the whole phase. Put a `BEGIN_COMMIT_OVERRIDE` block in the PR body; after merging, confirm the entries appear in the release PR.

## What Phase 2.3 shipped
- `Examples` / `NotFor` in attributes, sent as a criterion object; object and array instructions and criteria; `state` helpers and the `JevContent` factories, with budgets and `ContentBenchmarks`. Merged as PR #58.

## What Phase 2.2 shipped
- `ZeroAlloc.Jev.Analyzers` reports every Jev diagnostic, and the generator reports none. New rules: JEV001–002 (Error: empty Choice or Score enum, from the SDK schema), JEV003 (Warning: blank text), JEV004 (Warning: a backticked name that matches no `State` member), JEV005 (Warning: the sketch's level and option ranges), JEV006 (Info: a Choice member without `[Criteria]`). JEV101–107 moved out of the generator, with #5–#11 fixed. Enum rules are local, so the IDE shows them. The analyzer also runs on generated code.
- Invalid sets get throwing stub properties, so command-line builds show the JEV error rather than CS9248.
- `ZeroAlloc.Jev.CodeFixes` inserts `[Criteria]` and `[Level]`. Its Roslyn `ImportAdder` workarounds are tracked in #51; upstream, dotnet/roslyn#85804 and a comment on #77119.
- The package ships the generator, analyzers and code fixes. `Microsoft.CodeAnalysis.CSharp.Workspaces` is pinned at `[5.0.0]`.

## What Phase 2.1 shipped
- `IJevClient.EvaluateAsync<T>` (string, `JsonElement`), `EvaluateUtf8Async<T>`, `EvaluateAsync<T, TState>` as default interface methods; `JevClient` overrides with a raw pooled-buffer path through `IJevApi.EvaluateRawAsync` and `JevRawSerializer`. Overload pairs without / with required `ct` (RS0026).
- `[JevQuestions(State = typeof(...))]` → `IJevQuestionSet<TSelf, TState>`; JEV107 for invalid State types (moves to the analyzers in 2.2).
- `JevClientOptions.Model`; value equality for `ProbabilityMap<T>`, `Choice<T>`, `Score<T>`.
- Allocation gates: typed round trip 3784 B / budget 4224 B (identical on linux-x64). Upstream: ZeroAlloc.Rest#362.

## What Phase 1.8 shipped
- The AOT smoke app enforces `AllocationGate` budgets (Parse 192 B, readers 0 B, `EvaluateAsync` 5120 B) and treats every warning as an error.
- `benchmarks/ZeroAlloc.Jev.Benchmarks` plus `benchmarks.yml` (org smoke gate; `smoke / benchmarks` not required); record the first full run in `docs/performance.md`.
- The generator is packed from `GetTargetPath`; `tests/ZeroAlloc.Jev.PackTests` checks the nupkg layout and dependencies.
- `trigger-website.yml` (user docs only); Phase 4.3 now targets the org website. Org issue ZeroAlloc-Net/.github#43.

## What Phase 1.7 shipped
- `tests/ZeroAlloc.Jev.Integration.Tests`: WireMock.Net 2.18.0 over real sockets, 15 tests, public API only, runs in the CI `build` job.
- `tests/ZeroAlloc.Jev.Live.Tests`: 6 live tests that run only with `JEV_LIVE=1` and the provider's key; `.github/workflows/live-smoke.yml` runs them on manual dispatch from the `live-api` environment.
- OpenRouter live evaluation passes with the default model `jev-latest` (run locally 2026-09-27).
- Maintainer to-do: create the `live-api` environment (deployment branches `main`), add `TYPESAFE_API_KEY` and `OPENROUTER_API_KEY`, dispatch **Live smoke**, then record below whether TypeSafe sends `Retry-After` and what the 422 body looks like.

## What Phase 1.6 shipped
- `JevClient` retries 429, 503/529, other 5xx, 408, network failures and client time-outs with exponential backoff through ZeroAlloc.Resilience 3.2.0's `[Retry]` on the internal `IJevApi`, honouring `retry-after-ms` and `Retry-After` capped by `MaxRetryDelay`; exhausted retries return the last `JevError`.
- Public options `MaxRetries` (2), `InitialBackoff` (500 ms), `MaxRetryDelay` (30 s), `Jitter` (true); `Timeout` is per attempt.
- `RetryAfterHeader` reads all RFC 9110 date forms, pivots RFC 850 years on the current date, clamps overflow and rejects non-finite `retry-after-ms`.
- Tracked: ZeroAlloc-Net/ZeroAlloc.Resilience#195 (the `JevClient.ThrowDeclined` unwrap is a workaround until it ships), ZeroAlloc-Net/ZeroAlloc.Resilience#197 (unused package dependencies), #38 (HLQ005 pragmas), #40 (`X-TypeSafe-Retry-Count` parity).
- Lesson (superseded 2026-09-30): this assumed merge commits. Phase PRs are squash-merged, and a plain title then drops the phase from the changelog; see Phase 2.4's lesson.

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
- None for Milestone 3's start.
- Still unknown until a TypeSafe live run (needs `TYPESAFE_API_KEY` and the `live-api` environment): whether TypeSafe sends `Retry-After`, the 422 body schema, and whether Phase 2.4's `BuiltQuestionSet_ParsesAKeyedChoice` passes.

## Recommended Next Step
Merge the `chore/audit-milestone-2` PR, then run `new-milestone` for Milestone 3 — .NET integration. Open maintainer items: the `live-api` environment and TypeSafe live run; record the performance baseline from a full Benchmarks run; release PR #63 (0.2.0) stays open until you choose to release.
