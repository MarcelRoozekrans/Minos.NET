# Milestone 4 Design — Patterns & docs

**Date:** 2026-10-02
**Milestone:** 4
**Stage:** milestone (one milestone, multiple phases)

## Goal

A C# developer can learn Jev from its own docs site and apply TypeSafe's documented patterns in idiomatic C#. The library adds only thin, allocation-free helpers, and only where hand-written code tends to go wrong. Original, runnable samples show guardrails, intent routing and re-ranking end to end. CI keeps the samples honest by replaying recorded real answers.

## Context from the brainstorm (2026-10-02)

- **What TypeSafe documents.** [Speculative fan-out](https://docs.typesafe.ai/patterns/fan-out.md), [confidence-gated routing](https://docs.typesafe.ai/patterns/confidence-routing.md), [composite scoring](https://docs.typesafe.ai/patterns/composite-scoring.md) and [intent routing](https://docs.typesafe.ai/patterns/intent-routing.md). All four are shown as plain user code.
- **What the official SDKs ship.** The [Python](https://docs.typesafe.ai/sdk/python/usage.md) and [JavaScript](https://docs.typesafe.ai/sdk/javascript/api.md) SDKs ship no pattern helpers.
- **How the patterns work.**
  - Fan-out is a multi-question request, which `[JevQuestions]` types and `JevQuestionSet` builders already make.
  - Routing reads `Confidence`. Its documented tiers are above about 0.9 to act, 0.5–0.9 to confirm or flag, and below 0.5 to send to a human ([confidence](https://docs.typesafe.ai/confidence.md)). Noul answers carry no confidence: the probability is the signal.
  - Composite scoring is a weighted sum of Scores, each normalized to 0–1 by its level count.
- **Cookbooks.** The [cookbooks index](https://docs.typesafe.ai/cookbooks.md) lists 19 cookbooks and states no license terms. Re-ranking runs on the CLERC legal dataset, with 3,565 passages.
- **Docs site.** The org site, ZeroAlloc-Net/.website, already serves 20 libraries. Each library has a Docusaurus app at `apps/docs-<name>` over a `repos/<name>` submodule whose `docs/` holds the Markdown.
- **Disclaimer.** The README already carries the "Not affiliated with TypeSafe AI" disclaimer.

## Decisions

Taken with the maintainer during the brainstorm on 2026-10-02.

| Question | Decision | Why |
|---|---|---|
| How much helper API | **Thin helpers.** Add a normalized Score value and a confidence-tier gate, both allocation-free, with a handful of members each. Fan-out, intent routing and the rest stay documented idioms. | The official SDKs ship none. Normalizing by level count and tiering confidence are where hand-written code goes wrong. Milestone 5 freezes the public API, so the surface stays small. |
| How samples relate to the cookbooks | **Original, inspired-by.** Our own C# samples for the same three recipes, each with a small dataset we write ourselves. Each credits and links its TypeSafe cookbook, and copies no text, code or data from it. | The reuse terms are unknown. Small built-in data keeps the samples self-contained. |
| How CI proves the samples run | **Replay recordings.** Each sample runs live with a key, or in replay mode from checked-in recordings of one real run, which one command can refresh. CI runs every sample in replay mode and checks its decisions. | Without a TypeSafe key in CI, replay is the only way to run the samples there. The recordings show real model behaviour, not invented numbers. |
| Who changes ZeroAlloc-Net/.website | **This session opens the PR** and follows the existing `docs-*` apps exactly. The maintainer reviews and merges it. | Keeps Phase 4.4 self-contained. |
| Phase order | The roadmap's last two phases swap content: the user guide (4.3) comes before the site, logo and README (4.4). | The site serves the finished `docs/`, so the content should exist first. |

## Definition of Done

- [ ] All planned phases complete.
- [ ] All tests passing: unit, generator, analyzer, integration and pack, plus every sample in replay mode. Live smoke still runs when `JEV_LIVE=1` and a key are set.
- [ ] Pattern helpers ship in the core.
  - They are a normalized Score value and a confidence-tier gate with overridable 0.5 and 0.9 defaults.
  - They are listed in `PublicAPI.Unshipped.txt` and have 0 B `AllocationGate` budgets.
  - The AOT smoke app exercises them with zero IL2xxx/IL3xxx warnings.
- [ ] Guides for all four documented patterns live in `docs/`: fan-out, confidence routing, composite scoring and intent routing. Their C# snippets compile in CI.
- [ ] Three original samples are C# projects under `samples/`: guardrails, intent routing and re-ranking.
  - Each credits and links the TypeSafe cookbook that inspired it.
  - Each runs live with a key, and in replay mode from checked-in recordings of a real run.
  - CI runs each one in replay mode and checks its decisions.
- [ ] The user guide in `docs/` covers every area below, and #16 is closed:
  - getting started;
  - every question type;
  - typed evaluation and builders;
  - DI and configuration;
  - logging and telemetry;
  - Native AOT;
  - patterns and samples.
- [ ] jev.zeroalloc.net serves the guide through `apps/docs-jev` and `repos/jev` in ZeroAlloc-Net/.website. A push to `docs/` on `main` updates it through `trigger-website.yml`.
- [ ] The README carries the unofficial-client disclaimer and the logo, and links the site.
- [ ] Every existing allocation budget is unchanged.

## Phases

1. **Phase 4.1: Pattern helpers and guides** — `Surface: Backend`
   - **Goal:** Ship two allocation-free helpers, a normalized Score value and a confidence-tier gate with overridable defaults. Write guides for the four documented patterns, with C# snippets that compile in CI.
2. **Phase 4.2: Cookbook samples** — `Surface: Docs`
   - **Goal:** Write original guardrails, intent-routing and re-ranking samples. Each runs live, or in replay mode from recorded OpenRouter answers, and CI checks each one's decisions in replay mode.
3. **Phase 4.3: User guide** — `Surface: Docs`
   - **Goal:** Write the user guide in `docs/`, following the org layout. It covers getting started, every question type, typed evaluation and builders, DI and configuration, logging and telemetry, Native AOT, and patterns and samples. This also closes #16.
4. **Phase 4.4: Docs site, logo and README** — `Surface: Docs`
   - **Goal:** Register the repository in ZeroAlloc-Net/.website so jev.zeroalloc.net serves the guide. Add a logo, and slim the README so it points at the site, keeping the disclaimer.

**Ordering.**
- Helpers come first, because the samples and the guide use them.
- Samples come before the guide, so the guide can link working code.
- The site comes last, because it serves the finished `docs/`.

## Dependencies on Prior Milestones

- **Milestone 2's typed API.** The helpers build on question sets, builders, `Choice<T>`, and `Score<T>.Expected` and `.Confidence`.
- **Milestone 3's DI and configuration.** The samples and the guide show the generic-host setup users actually run, as the roadmap's M3 → M4 ordering rule says.
- **Milestone 1's test harness.** The replay seam reuses the existing handler-injection points, `ConfigurePrimaryHttpMessageHandler` and `JevClient(HttpClient, ...)`, so no new transport API is needed.

## External Constraints

- **No TypeSafe key is available.** Recordings come from OpenRouter, which serves Jev through `JevProvider.OpenRouter`. The samples say which provider, model and date produced their recordings.
- **Phase 4.4 lands partly in ZeroAlloc-Net/.website.** It needs a PR there and the `WEBSITE_DISPATCH_TOKEN` secret that `trigger-website.yml` already references.
- **The reuse terms for TypeSafe's cookbooks are unknown.** The samples are original work that links its sources.

## Risk Areas

| Risk | Impact | Mitigation |
|---|---|---|
| The helpers grow into a framework just before Milestone 5's API freeze | API to maintain, or to break at 1.0 | Two helpers, each a handful of members. Anything else stays a guide. |
| Recorded answers go stale, or OpenRouter's Jev differs from TypeSafe's | Samples teach thresholds that real answers miss | Recordings name their provider, model and date. One command re-records them. Samples assert their decisions, not exact numbers. |
| Recordings capture an API key or private text | A secret leaks in a public repository | The recorder stores only response bodies, plus the model and date, never headers. The sample data is authored and public. A test scans the recordings for key patterns. |
| Guide snippets drift from the API | Broken docs | Snippets compile in CI. |
| The .website PR waits on review | Phase 4.4 is blocked | 4.4 is last, and nothing in this repository depends on it. |

## Open Questions

None. The maintainer settled every question during the brainstorm on 2026-10-02. Phase-level detail goes to each phase's own brainstorm. That includes the helpers' exact names, the replay seam's shape and the guide's page list.
