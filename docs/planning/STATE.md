# Session State — 2026-10-02 (Phase 4.2 complete, PR to open)

**Date:** 2026-10-02

## Current Position
- **Milestone:** 4 — Patterns & docs, active since 2026-10-02 (design `docs/superpowers/specs/2026-10-02-milestone-4-design.md`).
- **Phase:** 4.2 — Cookbook samples is complete on branch `phase/4.2-cookbook-samples`; it lands through a PR to `main`. Phase 4.1 merged as PR #80; release 0.3.0 (#81) carried it. Release PR #63 (0.2.0) also merged; releases are now cut, NuGet publishing still off (#29).
- **Next task:** after the Phase 4.2 PR merges, confirm release-please lists its two `docs`/`test` entries, then run `start-next-phase` to brainstorm Phase 4.3 — User guide.

## What Phase 4.2 shipped
- `samples/ZeroAlloc.Jev.Samples.Shared`: `SampleMode` (replay default, live, record), `ReplayHandler` (primary handler, answers by SHA-256 of the request body, fails with the re-record command), `RecordingHandler` (successful response bodies only, never headers), `RecordingSession` (throws on mixed models), `RecordingsFile` (bodies stored as raw JSON, LF-only), `SampleHost` (wires a mode onto `AddJevClient`; replay supplies a placeholder key). Live runs need no checkout; replay and record read and write the recordings in the clone.
- Three original samples recorded on OpenRouter (`typesafe/jev-1.13-20260917`, 2026-10-02), each crediting and linking its TypeSafe cookbook:
  - Guardrails: 15 authored messages, Strict and Lenient policies. Ruling: advice requests are review-only (`BlockAt` null); severity still blocks. g15 (indirect instruction probe, 0.61) is where the policies part on recorded answers: Strict blocks, Lenient reviews — a 0.01 margin, documented.
  - Intent routing: 12 travel requests; 8 of 12 need no language model. "asdf" came back as high-confidence Other, so it reaches a person by intent, not by the low-confidence rule (documented).
  - Re-ranking: 25 authored help-centre articles, keyword shortlist of 8, one fan-out request per query with a keyed Noul per candidate built at run time; hit@1 1/5 to 5/5, hit@3 2/5 to 5/5 (a small corpus and a deliberately weak baseline, documented).
- `tests/ZeroAlloc.Jev.Samples.Tests` (139 tests): every rule pinned on canned answers, every recorded decision pinned, report snapshots via ZeroAlloc.TestHelpers 1.5.0 `TextSnapshot.VerifyText`, and a recordings safety scan (key shapes, auth headers case-insensitive, exact entry shape).
- CI (maintainer request): the `build` job runs every discovered sample's real entry point in replay mode with an empty key and `timeout 120s`. Other ZeroAlloc repos only run their AotSmoke apps.
- Upstream: ZeroAlloc.TestHelpers #59 (`TextSnapshot`, BCL-only) filed and shipped in 1.5.0 by the org session.

## What Phase 4.1 shipped
- Core: `ConfidenceTier { Low, Medium, High }` and `readonly struct ConfidenceThresholds`. Its `default` means 0.5 and 0.9, the cut points TypeSafe's confidence guide uses in an example, so an unset value never classifies every answer as High; a value on a threshold goes to the higher tier and NaN is Low. `Normalized` on `Score<T>` and `KeyedScore`: Expected / (levels − 1), clamped, 0 below two levels.
- AOT: the `PatternHelpers` gate holds `Classify` and both `Normalized` properties to 0 B per call; the check tells a strict instance from the default.
- Docs: guides for fan-out, confidence routing, composite scoring and intent routing in `docs/patterns/`, with an index and a `## Patterns` README section. Every C# block comes from a `#region` in `tests/ZeroAlloc.Jev.Docs.Tests`, written in by MarkdownSnippets 28.5.0 (`mdsnippets.json`, local tool); each example runs against canned answers, a test fails when a snippet block has more than one source, and CI runs `dotnet mdsnippets` then `git diff --exit-code`.
- Maintainer ruling at the final review: the guides use our own example data (app-store reviews, shop refunds, pull-request rubrics, an IT helpdesk); TypeSafe's example data has no known reuse terms. Each guide links TypeSafe's pattern page.
- Lessons: MarkdownSnippets excludes directories only by bare name, and reads snippet regions from any untracked text file, including `.superpowers` review diffs, so `ExcludeSnippetDirectories` lists `.superpowers`.
- Issue filed: #79 (the relative AOT allocation gates compare two noisy measurements with zero headroom; one flaked once during verification).

## What Phase 3.4 shipped
- Core: public `JevClientOptions.Validate()` runs `JevClientSettings.Resolve`, the check every constructor runs, so it throws what the constructor would throw. The invalid-option cases live in one shared test source, `InvalidOptionsCases`.
- DI:
  - Every `AddJevClient` registration adds an internal `JevClientOptionsValidator` for its options name, plus `ValidateOnStart()`. A generic host fails at `StartAsync`; without a host the first resolve throws `OptionsValidationException` with the core's message.
  - New overloads `AddJevClient(IConfiguration)` and `AddJevClient(string name, IConfiguration)`. Binding is source-generated through `EnableConfigurationBindingGenerator`. New dependency `Microsoft.Extensions.Options.ConfigurationExtensions` 10.0.0.
- Plan-probe finding: `TryAddEnumerable` keeps one validator for every options name, because it compares implementation types. The validator is added on the first registration of a name instead.
- Behaviour changes, documented in the README:
  - Options are still validated when the app registers its own `IJevClient`; a test host needs a placeholder key.
  - Creating Jev's named `HttpClient` from the factory directly needs valid options, an API key included.
  - A configuration value the binder cannot convert fails at the same moment with the binder's `InvalidOperationException`.
- Cost: a client bound from configuration measures 4376 B per call under Native AOT, equal to a hand-built client, checked by a relative gate. Existing budgets are unchanged.
- ZeroAlloc.Validation.Options was dropped from the milestone; startup validation reuses the core's own rules.
- Maintainer decision still open: config errors carry the core's `(Parameter 'options')` suffix, which the spec keeps.

## What Phase 3.3 shipped
- New package `ZeroAlloc.Jev.DependencyInjection` with four overloads, all returning `IHttpClientBuilder`:
  - `AddJevClient()` and `AddJevClient(Action<JevClientOptions>)` register the default `IJevClient` singleton.
  - `AddJevClient(name)` and `AddJevClient(name, configure)` register keyed singletons, injected with `[FromKeyedServices(name)]`.
- How each registration is wired:
  - Named options: the default client uses `Options.DefaultName`, a keyed client uses its key.
  - A named `HttpClient`, `ZeroAlloc.Jev` or `ZeroAlloc.Jev:{name}`, with a pooled `SocketsHttpHandler` (2 min) and an infinite handler lifetime, configured through `JevClient.ConfigureHttpClient`.
  - The `HttpClient` is configured on the first registration of each name only, guarded by a private marker service. The client is registered with TryAdd, so an `IJevClient` the app registered itself wins.
- Maintainer decision: `AddJevClient` removes the factory's request loggers, which cost 344 B per call (4720 vs 4376 B). `.AddDefaultLogger()` on the builder restores them.
- Core: public `JevClient.ConfigureHttpClient(HttpClient, JevClientOptions?)` applies the timeout, the base address (only when none is set) and the User-Agent, and needs no API key. `EnsureValidTimeout` rejects a time-out above `int.MaxValue` ms up front, which fixed a half-configured client and a leaked owned client.
- Cost: a DI-resolved call measures 4376 B under Native AOT, equal to a hand-built client. A relative gate and an absolute 4864 B gate check this. Existing budgets are unchanged.
- The pack fixture no longer packs with `--no-build`, which used to let a stale build pass.
- The milestone's ZeroAlloc.Inject and ZeroAlloc.Rest.DependencyInjection dependencies were dropped as unneeded.
- Issues filed: #73 (adopt ZeroAlloc.TestHelpers#56's measuring API, implemented in TestHelpers PR #57 but not released yet) and #74 (hand-rolled zero-allocation tests can flake under load).
- Lesson: the ZeroAlloc org session is `zeroalloc-0f` this time; check ListAgents for its current name before messaging it.

## What Phase 3.2 shipped
- Internal `[Instrument("ZeroAlloc.Jev")] IJevOperations`, with four methods, implemented by `JevOperations` over the retry proxy. `JevClient` calls the generated `JevOperationsInstrumented`. Source and meter are both `ZeroAlloc.Jev`. No constructor changes.
- One GenAI CLIENT span per operation, `evaluate {model}` or `list_models`, with start tags and success/failure end tags. Failures get `error.type` = the `JevErrorKind` name and Error status with no description. The span nests each retried attempt's `ZeroAlloc.Rest` span.
- Metrics: `gen_ai.client.operation.duration` in seconds, the GenAI token histograms and counters (`gen_ai.token.modality=text`), and `jev.answer.confidence`, one point per Choice or Score answer. Each has explicit buckets.
- `Evaluated<T>` defers the model, usage and confidence reads from the pooled response until the proxy asks, which it does only while something listens. `GeneratedQuestionCount<T>` is now read on every typed call and is 0 for a malformed hand-written set.
- Cost with nothing listening:
  - 0 B on the raw path, list-models and synchronously completing typed and built-set calls.
  - About 211 B on an asynchronously completing typed or built-set call, for the unwrap. The limit is 344 B.
- Cost while listening: about 1.0–1.8 KB per call. New AOT budgets: 6272, 5440 and 5760 B while listening, and 5056 B for an async typed call with telemetry off. Existing budgets are unchanged.
- The AOT smoke app's yielding measurements now take the median of five runs, because the least of three made a logging check flaky.
- New runtime dependency ZeroAlloc.Telemetry 1.10.0. Its generator reaches consumers transitively and stays inert, which the pack tests assert.
- Upstream: ZeroAlloc-Net/ZeroAlloc.Telemetry#184 is filed by the ZeroAlloc org session. On the exception path the span gets the exception message as its description and no `error.type`. The README documents this. #68 has a comment on the third loose budget, `EvaluateBuiltSetRoundTrip`: 4736 B against 3656 B measured.
- Lesson: the plan quoted a stale 4288 B for the built-set call. `main` measured 3656 B. Re-measure baselines on `main` before a plan asserts them.

## What Phase 3.1 shipped
- `JevClient(JevClientOptions?, ILoggerFactory?)` and `JevClient(HttpClient, JevClientOptions?, ILoggerFactory?)`; category `ZeroAlloc.Jev.JevClient`; the four older constructors log nothing. `new JevClient(null, null)` is now CS0121 (accepted; it always threw).
- Six source-generated `[LoggerMessage]` events in internal `JevLog`, ids 1001–1006, none with more than six fields (a larger one generates a struct HLQ006 rejects). Success at Debug, failures and retries at Warning, unexpected exceptions at Error.
- Retried attempts are logged by the internal `LoggingJevApi` decorator between the ZeroAlloc.Resilience proxy and the transport, using the proxy's own `RetryPolicy`.
- Privacy: no state, instructions, criteria, answers, API key, headers or `JevError.Detail` in logs. `Network` and `InvalidResponse` messages are logged as a fixed text, because they can quote the request or response; the privacy tests found and closed a real leak of exception text on the network path.
- Cost: nothing with no logger or every level disabled, proven by an async discriminating check in the AOT smoke app (5254 B per call either way). An enabled logger adds 0 B on a synchronous call and about 480 B on a truly asynchronous one.
- New runtime dependency `Microsoft.Extensions.Logging.Abstractions` 10.0.0; its `[LoggerMessage]` generator reaches consumers transitively (NuGet/Home#6720) and stays inert.
- Issues filed: #67 (remove `Json = true` from attributes, deferred to Phase 5.1), #68 (tighten two AOT budgets, needs a linux-x64 re-measurement).

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
Merge the Phase 4.2 PR, check release-please counted it, then run `start-next-phase` for Phase 4.3 — User guide.




Open maintainer items:
- the `live-api` environment and the TypeSafe live run;
- record the performance baseline from a full Benchmarks run;
- decide #68, which now covers three gates;
- answer ZeroAlloc.Telemetry#184's two API questions;
- decide whether to report to dotnet/runtime that IHttpClientFactory's request loggers allocate 344 B per call even when no logger is enabled;
- Renovate PRs #70 and #71;
