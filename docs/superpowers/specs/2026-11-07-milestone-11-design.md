# Milestone 11 Design — `Microsoft.Extensions.AI decision adapters`

**Date:** 2026-11-07
**Milestone:** `11`
**Stage:** milestone (one milestone, multiple phases)
**Status:** approved 2026-11-07

> Follows Milestone 10, which ended with a dotnet/extensions proposal draft. While rechecking it on 2026-11-07 the maintainer chose to pick up the upstream work in the next milestone, and chose "adapters both ways" (not adopting upstream types, not a mapping document alone) and a phased plan whose shipping waits on upstream.

## Goal

A .NET app can use Minos and the Microsoft.Extensions.AI decision abstraction together. A Minos `IDecisionClient`, with its pipeline of escalation, fallback, retries, caching and recording, can be handed to code that expects the upstream `IDecisionClient`. A provider that implements the upstream interface can sit behind Minos, so `UseEscalation` and `UseFallback` work over it. The upstream types are still experimental and unreleased, so the milestone builds the mapping and the adapters now, and publishes the package only when upstream ships a package that contains them.

## Why

- dotnet/extensions has an open proposal (issue 7764) and an experimental implementation (PR 7795, Layer 1; PR 7796, Layer 2) for the same idea Minos implements: bounded typed questions in, probabilities out. If it lands, it becomes the type apps and providers are written against.
- Minos's value is then the part upstream leaves out of Layer 1: the pipeline, the providers' protocols, the calibration and the recorder. A bridge keeps that value usable over the standard type, and lets a user try upstream providers without leaving Minos.
- `Minos.NET.Extensions.AI` already bridges the other way, an `IChatClient` behind a Minos client. This closes the matching gap for decision clients.

## Facts checked on 2026-11-07

- Issue 7764 is open, last updated 2026-10-12, 9 comments. PR 7795 is open, mergeable, review required, no milestone, last updated 2026-10-21 (head `02e54f02`, 11 commits). PR 7796 is open.
- The latest stable `Microsoft.Extensions.AI.Abstractions` is 10.11.0 and it has no decision types. All new upstream types are experimental (`MEAI001`).
- Upstream shape: `IDecisionClient.GetResponseAsync(DecisionRequest, DecisionOptions?, CancellationToken)` returning `DecisionResponse`, plus `GetService`. Questions are `Binary`, `Choice` and `Score`. A typed `DecisionDefinition<TResult>` binds questions to a result type. Failures are exceptions.
- Minos shape: `IDecisionClient.EvaluateAsync(DecisionRequest)` returning `Result<DecisionResponse, DecisionError>`, with `Noul`, `Choice` and `Score` questions, generated question sets, and a pipeline.

## Goals

- A mapping, written down and tested, between the Minos and upstream request, question, answer and error types, with each difference named.
- Adapter in each direction, with the behaviour pinned by tests: Minos over an upstream client, and an upstream client over a Minos client.
- Escalation and fallback keep working over an upstream-backed client, including the path on the response.
- Everything that depends on the unreleased types stays unpacked until an upstream package exists.

## Non-goals

- Replacing Minos's types with upstream ones (a possible 2.0 once upstream is stable; not this milestone).
- Implementing upstream Layer 2 (function and routing composition) or competing with it.
- Posting the dotnet/extensions comment. The maintainer posts the approved draft.
- Any change to the Minos public API beyond what the adapters need. A needed change is its own decision, taken in the phase that finds it.

## Anti-goals

- A package that references a private or locally built copy of upstream and ships that way.
- Copying the upstream contracts into Minos under another namespace (rejected: it forks a moving API).
- Hiding a difference. Where the two models cannot match, the adapter says so with an error or a documented loss, never a silent guess.

## Approaches considered

1. **Phased, shipping gated on upstream (chosen).** Mapping first, adapters built against a local build of the PR head and kept unpacked, publish as a beta once a package exists. The milestone can close with the publish deferred. It keeps the work useful to the proposal discussion and costs little if upstream changes.
2. **Mapping and gap analysis only, adapters later.** Nothing is built that upstream might change. Rejected: it leaves the adapters' hardest questions (errors, the path, calibration) unanswered until upstream moves, and the maintainer wanted the adapters.
3. **Mirror the upstream types in Minos.** Rejected as an anti-goal above.

## Definition of Done

- [ ] All planned phases are complete.
- [ ] All tests pass, including the AOT smoke and surface checks and the docs tests, with zero IL2 and IL3 warnings.
- [ ] A mapping document on the docs site names every difference between the Minos and the upstream decision types, and each one has a test that pins the adapter's behaviour: question kinds, state, answers and distributions, errors, options, `GetService`, cancellation and disposal.
- [ ] A Minos client wrapped as an upstream `IDecisionClient`, and an upstream client wrapped as a Minos one, both pass the Minos conformance checks that apply to them, and a round trip returns the same answers.
- [ ] `UseEscalation` and `UseFallback` run over an upstream-backed client in a test, and the path and answered-by fields survive the wrap.
- [ ] The adapter package builds against a local build of the upstream PR head in CI or a documented script, and stays unpacked, with a test that fails if it is packed before an upstream package exists.
- [ ] The maintainer has a short upstream-facing gap list (what upstream's Layer 1 lacks for a pipeline like Minos's), for use when posting the comment on issue 7764.
- [ ] If an upstream package with the decision types exists by the end of the milestone: the package ships as a beta in a minor release through release-please, with ApiCompat clean. Otherwise the publish is a tracked issue, and that is not a gap.

**Out of scope:** the posting of the comment, adoption by Daedalus.NET, upstream Layer 2, and a Minos 2.0.

## Phases

1. **Phase 11.1: Mapping and gap analysis** — `Surface: Docs`
   - **Goal:** A tested mapping between the Minos and the upstream decision types, a page on the docs site, and a short gap list for the upstream discussion.
2. **Phase 11.2: Adapters** — `Surface: Backend`
   - **Goal:** The two adapters in a new unpacked package, with conformance, round-trip and pipeline tests, built against a local build of the upstream PR head.
3. **Phase 11.3: Publish when upstream ships** — `Surface: Infra`
   - **Goal:** Pack and release the package as a beta against the first upstream package that contains the types, or record the wait as an issue and close the phase as deferred.

## Decisions I made (the maintainer can change them)

- **New package, not the existing one.** `Minos.NET.Extensions.AI.Decisions` (working name), not an addition to `Minos.NET.Extensions.AI`: that package targets stable `IChatClient` and has a 1.5.0 baseline, and this one depends on an experimental API.
- **Errors.** Upstream throws; Minos returns `Result`. The Minos-over-upstream adapter maps exceptions to `DecisionError` kinds, with cancellation and disposal passing through. The upstream-over-Minos adapter throws a documented exception type that carries the `DecisionError`.
- **Experimental on both sides.** The package carries its own experimental marker and says in its README that it follows an unreleased API.
- **Local build of the PR head.** Pinned by commit, in a documented script. The commit is bumped only on purpose.

## Dependencies on Prior Milestones

- Depends on: Milestone 6's `IDecisionClient`, Milestone 10's path on the response, and the conformance suite from Milestone 6.
- External: upstream PR 7795 reaching a released package. Nothing in this milestone's Definition of Done waits on that except the last item.

## External Constraints

- Upstream's API is experimental and may change or be dropped.
- The maintainer posts the dotnet/extensions comment, and only after rechecking issue 7764 and PR 7795.

## Risk Areas

| Risk | Impact | Mitigation |
|---|---|---|
| Upstream reshapes the API after the adapters are built | Rework in 11.2 | The mapping is the stable asset; the adapter is small; the commit is pinned and bumped on purpose |
| Upstream never ships Layer 1, or ships it differently | 11.3 never happens | The milestone closes with 11.3 deferred and tracked; 11.1 and 11.2 still serve the discussion |
| The models cannot match, for example Minos's Noul against upstream's Binary, or calibration metadata | Silent loss in the wrap | Name each difference in 11.1 and fail loudly in the adapter, with a test per difference |
| Layer 2 overlaps `UseEscalation` | Two ways to do routing | State the overlap in the gap list; do not implement Layer 2 |

## Open Questions

- None that block planning. The package name and the exception type are my calls above; the unpacked-until-upstream rule is the maintainer's choice.
