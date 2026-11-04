# Milestone 10 Design — `Escalation`

**Date:** 2026-11-04
**Milestone:** `10`
**Stage:** milestone (one milestone, multiple phases)

> This details Milestone 10 of the roadmap design (`docs/superpowers/specs/2026-10-09-roadmap-design.md`) and implements #119, including its 2026-10-28 update: escalating to a chat model is one more `IDecisionClient`, through `Minos.NET.Extensions.AI`, so escalation is built over `IDecisionClient` alone.

## Goal

A .NET app decides on a local decision server first and hands only the questions the local model is unsure of to a stronger decider, within the time it has. When a provider fails, the next one in a fallback chain answers. Each answer says which path produced it. Both are `IDecisionClient` pipeline stages that compose with DI and keyed clients. A ticket-triage sample shows the split between local and escalated answers, and a guide teaches confidence routing as a framework feature.

## Why

- Milestone 9 made a local model usable and measurable, and it deferred automatic hosted-or-local switching to this milestone.
- Daedalus.NET, the first workload, already does a crude version by hand. Below a 0.6 confidence on its demand Score it gives up and uses the node's default model (`NodeSelectionMapper.LowConfidence`). Escalation replaces "give up" with "ask a better decider".
- The field is converging on "fast decision first, escalate when unsure", and Microsoft.Extensions.AI has no decision primitive yet.

## Decisions

Made by the maintainer on 2026-11-04:
- **Daedalus.NET is the first workload, and its requirements are binding.** It makes one decision, `NodeQuestions`: one Score (demand) and six Noul questions (tool families). The whole decision runs within one budget, `Thalos:Decisions:Timeout`, 5 s by default, which covers the host's preparation, the Minos call and the host's check. Task text may only go to a zero-data-retention account or a local server.
- **Direction: local first, then hosted.** The local decision server answers first. Questions below their threshold go to the zero-data-retention hosted decider.
- **Escalate only when time is left.** A step that would not fit the time remaining is skipped. The caller then gets the best answer so far, marked as not escalated and with the reason. Daedalus keeps its 5 s budget.
- **Thresholds are set in the escalation options, by question key,** with a default for keys not listed. They can come from configuration, as Daedalus's do now. No generator or attribute change.
- **Two stages in the core, `UseEscalation` and `UseFallback`,** in `Minos.NET/Pipeline`, next to `UseRetries` and `UseLogging`. Not one routing stage, and not a new package.
- **The dotnet/extensions proposal is a draft.** The milestone delivers a reviewed proposal draft in the repository, with no private details. The maintainer posts it, or tells me to. Done means the draft is approved, not that it is posted.
- **The escalated answer wins** over the local one, even when it is less confident (decided after the builder check).
- **Daedalus adopting escalation is outside the Definition of Done,** as its adoption of the recorder was in Milestone 9. The ticket-triage sample is the proof.

## Approaches considered

1. **Two core stages, `UseEscalation` and `UseFallback` (chosen).** They match the existing pipeline and M.E.AI's style, so they map cleanly to the dotnet/extensions draft. They bring no new dependency, and retries already live in the core. The trade-off is that the order they compose in matters, which the guide has to explain.
2. **One routing stage**, such as `UseRouting(r => r.Then("hosted", on: LowConfidence | Failure))`. It keeps the deadline and the path in one place, with no composition order to get wrong. But it is a new concept with no M.E.AI counterpart, so it is harder to propose upstream, and 10.1 and 10.2 would merge into one larger phase.
3. **Either shape in a new `Minos.NET.Routing` package.** It keeps the core small, but it adds a package to release, inspect and AOT-gate, and it brings no dependency the core lacks.

## Binding requirements, carried from Milestone 9

- **Off by default.** Nothing escalates or falls back unless the stage is registered.
- **Decorate, never replace,** the app's existing `IDecisionClient` registration. Targets are keyed `IDecisionClient`s.
- **No state in logs, traces or exception messages.** This includes the escalation and fallback events.
- **Native AOT clean:** zero IL2 and IL3 warnings, no reflection-based System.Text.Json. Daedalus's CI ratchet blocks increases.
- **No added latency on the path that answers,** beyond the escalation call itself when one is made.
- **Privacy is the app's configuration, not the middleware's guess.** The stage sends to whichever target the app registered. The guide says a target must meet the same data rules as the primary.

## My calls

1. **What "unsure" means for each question kind.** A Score or Choice is unsure when its `Confidence` is below the key's threshold. A Noul is unsure when its probability is close to 0.5, measured as `max(p, 1 − p)` below the threshold. A Noul at 0.05 is as sure as one at 0.95.
2. **Escalation re-asks only the unsure questions.** It builds a `QuestionSetDefinition` from just those questions, which the public constructor allows, sends it to the target with the same state and images, and merges the answers by key into one `DecisionResponse`. Confident local answers are kept. A definition built for the subset is cached per set of keys, so a repeated pattern does not allocate a new one each time.
3. **The path is recorded per question.** The response carries which client answered each question, local or escalated, and why a question was not escalated: none was needed, time ran out, or the target failed. Telemetry tags the span with the path and counts escalations. The recorder stores the path with the record, because a shadow or calibration report needs to know which model's answer it is grading. This is new public API in the core, reviewed in the 10.1 spec and shipped in a minor version.
4. **The time check.** The caller's `CancellationToken` carries no deadline, so the remaining time cannot be read from it. The stage takes a per-call deadline (proposed: an optional `DecisionRequest.Deadline`), with a stage option as the fallback when none is set. It also takes an expected latency per target, configured or measured as a running median. Escalation, or the next fallback step, runs only when the expected latency fits the time remaining. The 10.1 spec settles the exact API.
5. **Decided by the maintainer after the builder check: the escalated answer wins.** The app chose the target as the stronger decider, so its answer replaces the local one even when it is less confident. The path records both confidences, so a report can show where the two disagreed.
6. **The fallback chain triggers on failure, not confidence.** That covers an error result that retries did not fix, a timeout, and a refused or unavailable provider. It skips a step whose expected latency does not fit, by the same time check. Retries stay inside each step, so `UseRetries` composes under it.
7. **Composition order, documented and tested:** `UseEscalation` outside `UseFallback`. Fallback makes each tier reliable, and escalation then moves between tiers. A test pins the documented order, and the guide shows it.
8. **The ticket-triage sample builds on the provider-switch sample,** which already routes eight support tickets with one Choice question. The new sample adds a larger ticket set, local first, escalation to the hosted decider, and a committed metrics report of the split: how many questions each path answered, the latency and the cost. Hosted runs need the maintainer's approval, as before. The builder can instead make it a new sample if extending the old one muddies it.
9. **The OpenAI step is not shown live.** #161 still has no OpenAI account. The chain is shown with the local server, TypeSafe and OpenRouter; the guide says OpenAI fits the same slot.
10. **Chat-model escalation is shown but not required.** `ChatDecisionClient` is one more target. The guide shows it with its `ProbabilitySource`. A `Verbalized` probability is weaker evidence than `Model` or `Logprobs`, and the guide says so.

## Settled after the builder check (my calls)

1. **The path is additive API.** `DecisionResponse` gains an init-only property: per question, the client key that answered and, when not escalated, the reason. A fallback-only response carries the same shape, with every question on the client that answered. No shipped constructor or type changes shape. The 10.1 spec names it, and an API spike opens that phase, because 10.2, 10.3, the recorder, telemetry and the proposal draft all build on it.
2. **The deadline is absolute,** a timestamp read from the stage's `TimeProvider`, so tests use a fake clock. It is not part of `DecisionRequest` equality or the cache key. When it is not set, the stage option is a budget measured from the stage's entry, and the guide says that ignores time the host spent before the call. The deadline gates whether a step starts, and also cancels a step that is still running when it passes. That cancellation is the "timeout" the fallback chain reacts to.
3. **Expected latency is configured only, per target key, in 10.1 and 10.2.** A measured running median is left out: it is shared mutable state, and nothing needs it yet. It can be added later without a breaking change.
4. **What triggers fallback is settled in the 10.2 spec,** from the existing `DecisionErrorKind` values. The starting rule: provider and transport failures move on, such as timeouts, rate limits, server errors, unavailability, authentication and refusals. Errors in the request itself are returned as they are, such as validation errors. A step that cannot take the request, such as `Unsupported` for images, is skipped. The caller's own cancellation always stops the chain.
5. **A merged response's metadata:** `Usage` sums both calls. `Model`, `Id` and `UpstreamProvider` are the primary's, and the per-question path names the target for each escalated answer. `IsCached` is true only when every answer came from the cache. Each answer keeps its own `ProbabilitySource`. The 10.1 spec pins this.
6. **The subset re-ask sends the original state and images,** not the local answers, and copies each question's definition unchanged, with its options and descriptions. A target sees an ordinary, smaller question set.
7. **A re-ask or a fallback step is a new logical request,** so it carries `RetryAttempt` 0.
8. **The sample's cost comes from the existing price table, `prices.json`.** Before the billed run, every model the sample is served is checked to have a price.

## Definition of Done

- [ ] All planned phases are complete.
- [ ] All tests pass, including the AOT smoke and surface checks and the docs tests, with zero IL2 and IL3 warnings.
- [ ] `UseEscalation` re-asks only the questions below their per-key threshold, against a keyed target client, and merges the answers. Tests cover each of these:
  - Score, Choice and Noul thresholds, including a Noul near 0 or 1 that does not escalate.
  - A confident answer is never re-asked.
  - When the time left is below the target's expected latency, escalation is skipped, the local answer is returned, and the reason is recorded.
  - A failing target leaves the local answer in place, with the reason recorded.
  - An escalated answer replaces the local one even when it is less confident, and the path records both confidences.
  - The path that answered each question is on the response, on the span, in the counters, and in the recorder's record.
  - No state appears in a log, a trace or an exception message.
- [ ] `UseFallback` moves through a chain of keyed clients on failure, composes with `UseRetries` and DI, skips a step that does not fit the time left, and records which client answered. The documented composition order with `UseEscalation` is pinned by a test.
- [ ] A ticket-triage sample runs local first with escalation on low confidence, and a committed report shows the split by path, with latency and cost. Each hosted run needs the maintainer's approval.
- [ ] A guide on the docs site teaches confidence routing as a framework feature: thresholds sized to the cost of being wrong, the time check, the fallback chain, composition order, privacy of targets, and chat-model escalation.
- [ ] A dotnet/extensions proposal draft for the decision abstraction is in the repository, with no private details, and the maintainer has approved it.
- [ ] The new API ships in a minor release through release-please, and ApiCompat is clean against the previous release.

## Phases

1. **Phase 10.1: Escalation middleware** — `Surface: Backend`
   - **Goal:** `UseEscalation` in the core: per-key thresholds, a partial re-ask merged by key, the time check, the path per question on the response, telemetry and the record (#119).
2. **Phase 10.2: Provider fallback chain** — `Surface: Backend`
   - **Goal:** `UseFallback` in the core: a chain of keyed clients on failure, such as local, then Clef, then a hosted decider, sharing 10.1's time check and path, composing with DI, retries and escalation (#119).
3. **Phase 10.3: Ticket-triage sample** — `Surface: Mixed`
   - **Goal:** local-first ticket triage with escalation on low confidence, and a committed report of the split by path, latency and cost, from an approved hosted run (#119).
4. **Phase 10.4: Confidence routing as a framework feature** — `Surface: Docs`
   - **Goal:** the confidence-routing guide on the docs site, and a dotnet/extensions proposal draft for the maintainer to post (#119).

## Dependencies on Prior Milestones

- **Milestone 6:** `IDecisionClient`, `DecisionClientBuilder` and its stages, keyed client registration, and the adapters.
- **Milestone 8:** the dataset format and `Minos.NET.Evaluation`'s metrics, for the sample's report.
- **Milestone 9:** the local decision server in `local-systemone.yml`, the recorder, which gains the path, and the Banking77-scale measurements of local latency.
- **Outside the milestones:** `Minos.NET.Extensions.AI` (1.5.0), with `ProbabilitySource`, for chat-model escalation.
- **External:**
  - The local server is ollajev 0.4.0, pinned below Python 3.13.
  - Hosted runs use TypeSafe and OpenRouter from the `live-api` environment.
  - Daedalus adopting escalation is Daedalus's work, not part of this Definition of Done.

## External Constraints

- **Daedalus's 5 s budget:** the local model's median is about 4 s per decision on a CPU runner (Phase 9.4), so on that hardware a local decision plus an escalation does not fit. The time check exists for this case. The guide says escalation needs a faster local server or a larger budget to pay off.
- **Native AOT:** Daedalus is moving to Native AOT, so the new stages must be AOT clean from their first release.
- **Privacy:** task text may only reach zero-data-retention or local endpoints. The app configures that; the stage never adds a target of its own.
- **Hosted runs are billed** and each needs the maintainer's approval.
- **The dotnet/extensions post is public and permanent.** The draft is checked for private details before the maintainer sees it.

## Risk Areas

| Risk | Impact | Mitigation |
|---|---|---|
| The partial re-ask and merge produce a response that differs subtly from a full answer, such as probability offsets or usage totals | Wrong answers or wrong cost reporting | Tests compare merged responses against full ones question by question. Usage is summed across both calls, and the 10.1 spec pins how. |
| The time check is too coarse, so escalation either never runs or overruns the caller | Escalation is useless, or the caller's budget is broken | Expected latency is configurable as well as measured. Tests use a fake clock and slow fakes, and the sample report shows how often escalation was skipped for time. |
| New core API, such as a per-call deadline or a per-question path, freezes too early | Breaking changes later | An API review in the 10.1 spec, ApiCompat, and a minor release. The dotnet/extensions draft comes last, after the API has been used in the sample. |
| Escalation changes which answer the recorder and shadow report grade | Misleading calibration | The record carries the path per question, and the report can split by it. |
| Escalated and local thresholds disagree with Daedalus's own floors | Double gating, or confusing behaviour | The guide shows Daedalus's case: the escalation threshold sits at or above the app's acting floor. |

## Open Questions

None blocking. These are settled in their phase specs:
- **10.1:** the exact API for the per-call deadline and the per-question path, how usage is summed, and whether calibrated thresholds from Milestone 8's reports can seed the per-key thresholds.
- **10.3:** whether the ticket-triage sample extends the provider-switch sample or is new, and the size of its ticket set.
