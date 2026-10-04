# Milestone 5 Design — 1.0 hardening

**Date:** 2026-10-04
**Milestone:** 5
**Stage:** milestone (one milestone, multiple phases)

## Goal

Ship ZeroAlloc.Jev 1.0.0 to NuGet. Before release:
- its public API has been reviewed and frozen;
- it has been measured against a hand-written .NET client and against TypeSafe's official JS and Python SDKs;
- it has passed against TypeSafe's real API and under Native AOT across its whole public surface.

## Decisions

The maintainer took these decisions during the brainstorm on 2026-10-03 and 2026-10-04.

| Question | Decision | Why |
|---|---|---|
| What the milestone ships | **1.0.0 on NuGet. The TypeSafe live suite must pass at least once before release.** | Shipping 1.0 is the "declared mature" moment that #29 waits for. Until now Jev has only run live against OpenRouter, and TypeSafe's `Retry-After` behaviour and 422 body are still unknown. |
| Benchmark comparisons | **The raw .NET baseline plus the official JS and Python SDKs**, all against one shared local mock server. | The maintainer chose the widest comparison. The network is taken out, so the numbers compare per-call client cost. |
| Versioned docs | **One live version, as the org does.** The guide states which package version it describes. | None of the 24 sibling docs apps uses Docusaurus versioning, including Mediator at 6.1.1. "Versioned" is dropped from the definition of done. |
| Open follow-up issues | **5.1:** #67, #23, #24, #25 and #85, and #21 closes. **5.3:** #68, #73, #74 and #79. **5.4:** #29 and #28. **Out of scope:** #51 and #38, both blocked upstream. | Breaking and behaviour changes land before the freeze. The allocation-measurement work fits the verification phase. Publishing and api-compat belong to the release. |

## Definition of Done

- [ ] All planned phases are complete.
- [ ] All tests pass: unit, generator, analyzer, DI, docs, integration, pack and samples, with every sample in replay mode. The TypeSafe live suite has passed at least once with a real key, as well as OpenRouter's.
- [ ] The public API is reviewed:
  - every public type is sealed or deliberately open;
  - naming and nullability are reviewed;
  - XML docs are complete;
  - #67, #23, #24 and #25 are resolved;
  - ZeroAlloc.Telemetry 1.11.0 is adopted (#85);
  - #21 is closed.
- [ ] `PublicAPI.Shipped.txt` describes 1.0.0. api-compat (#28) checks every later change against the 1.0.0 package.
- [ ] A published benchmark suite compares Jev against:
  - a hand-written raw `HttpClient` plus System.Text.Json client;
  - TypeSafe's official JS SDK;
  - TypeSafe's official Python SDK.

  All four call one local mock server that serves recorded Jev responses. The results live in `docs/performance.md` and on jev.zeroalloc.net, with each runtime and version and what each number measures.
- [ ] Native AOT and trim verification covers the whole public API with zero IL2xxx and IL3xxx warnings.
- [ ] The model aliases `jev-latest` and `jev-preview` are checked live.
- [ ] The allocation-measurement follow-ups are closed: #68, #73, #74 and #79.
- [ ] 1.0.0 is published to NuGet by release-please and a publishing workflow (#29). The guide states the version it describes, and the README's Status line no longer says the package is unpublished.
- [ ] Every allocation budget is unchanged or tightened, never loosened.

## Phases

1. **Phase 5.1: Public API review.** `Surface: Refactor`
   - **Goal:** review the whole public surface: sealing, naming, nullability, defaults and XML docs. Make the breaking changes before the freeze: #67, #23, #24 and #25. Adopt ZeroAlloc.Telemetry 1.11.0 (#85), and close #21.
2. **Phase 5.2: Benchmark suite.** `Surface: Backend`
   - **Goal:** add a raw .NET baseline in BenchmarkDotNet, plus Node and Python harnesses that run TypeSafe's official SDKs. Every client calls one local mock server that serves recorded Jev response bodies. Publish the results.
3. **Phase 5.3: Live, AOT and alias verification.** `Surface: Infra`
   - **Goal:**
     - Pass the TypeSafe live suite, and record whether TypeSafe sends `Retry-After` and what its 422 body looks like.
     - Check the `jev-latest` and `jev-preview` aliases live.
     - Verify Native AOT and trimming across the whole public API.
     - Close #68, #73, #74 and #79.
4. **Phase 5.4: 1.0 release.** `Surface: Infra`
   - **Goal:**
     - Add the NuGet publishing workflow (#29), which packs and inspects the package before publishing, and publishes only from a release-please release.
     - Cut 1.0.0.
     - State the version in the guide, and update the README's Status line.
     - Then add api-compat (#28) against the 1.0.0 package.

**Ordering.**
- 5.1 comes first, so the benchmarks and the AOT verification measure the frozen API.
- 5.2 and 5.3 are independent of each other.
- 5.4 comes last. It cannot start its release step until 5.3's TypeSafe run has passed.

**Update 2026-10-04 (maintainer decision during Phase 5.3's brainstorm):** the old Phase 5.3 is split. 5.3 is now "AOT, trim and measurement verification", which needs no key. A new 5.4, "Live and alias verification", holds the TypeSafe live run and the alias checks, and starts when the maintainer has a TypeSafe key. The 1.0 release moves to 5.5. The definition of done is unchanged.

## Dependencies on Prior Milestones

- **Milestone 1:**
  - the live test harness, with `JEV_LIVE=1`, the `live-api` environment and the Live smoke workflow;
  - the release-please pipeline;
  - the pack tests.
- **Milestones 2 to 4:**
  - the public API under review;
  - the AOT smoke app and its `AllocationGate` budgets;
  - `benchmarks/ZeroAlloc.Jev.Benchmarks`;
  - jev.zeroalloc.net, where the benchmark results are published.
- **Milestone 4's recordings:** the `RecordingsFile` format provides real Jev response bodies for the benchmark mock server, so no answers are invented.

## External Constraints

- **A TypeSafe API key.** The maintainer obtains one, either into the `live-api` environment or locally. 1.0 cannot ship without a passing TypeSafe run.
- **NuGet publishing.** The org's `NUGET_API_KEY` is already visible to this repository. The 5.4 workflow is the deliberate step that STATE.md requires before anything is published.
- **The official SDKs.** The harnesses install them as pinned dependencies and copy none of their code. 5.2 checks their licences, and confirms that each SDK can target a local base URL.
- **Breaking changes.** These are allowed in 5.1, because no version has been published to NuGet. Each one is recorded as a breaking changelog entry.

## Risk Areas

| Risk | Impact | Mitigation |
|---|---|---|
| No TypeSafe key arrives | 1.0 is blocked | 5.2 and the rest of 5.3 go ahead. 1.0 waits, as the maintainer decided. The milestone records the blocker rather than shipping unverified. |
| The cross-runtime benchmarks mislead | Readers draw unfair conclusions | Every client calls the same local mock server with the same recorded bodies. The page reports per-call time and memory with the network removed. It names each runtime and version, and says what each number does and does not measure. |
| An official SDK changes or cannot target a mock server | The harnesses break | Pin the SDK versions. If an SDK cannot be pointed at a local base URL, record that and drop that comparison, rather than patching the SDK. |
| The API review finds more than the known issues | 5.1 grows | Each breaking finding is fixed in 5.1, before the freeze. An additive finding is fixed in 5.1 too, or filed as a post-1.0 issue if it is its own sizeable piece of work. |
| The first real publish goes wrong | A bad package on NuGet, whose versions cannot be deleted | 5.4 packs and inspects the package in CI before publishing, and publishes only from release-please's release. If a bad version ships, unlist it and fix forward. |

## Open Questions

None. The maintainer settled every question during the brainstorm. Phase-level detail goes to each phase's own brainstorm, including the exact API changes, the mock server's shape and the publishing workflow.
