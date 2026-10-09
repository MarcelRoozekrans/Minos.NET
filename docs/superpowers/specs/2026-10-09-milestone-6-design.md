# Milestone 6 Design — `Provider-neutral core`

**Date:** 2026-10-09
**Milestone:** `6`
**Stage:** milestone (one milestone, multiple phases)

> Follows the roadmap revision in `docs/superpowers/specs/2026-10-09-roadmap-design.md` and the maintainer's direction in `docs/plans/2026-10-09-provider-neutral-direction.md`. Issues: #120 (name), #115 (neutral model and adapters), #116 (conformance part), #119 (`IDecisionClient` core).

## Goal

One typed question set runs against TypeSafe, OpenRouter, OpenAI and a local `/v1/systemone` server by configuration alone, under a vendor-neutral name and behind an `IDecisionClient`. The library stops being a TypeSafe Jev client and becomes a provider-neutral decision client. Its public API reaches the shape that Milestone 7 reviews and freezes. Nothing is published in this milestone.

## Definition of Done

- [ ] All planned phases complete.
- [ ] All tests pass: unit, generator, analyzer, DI, docs, integration, pack, samples in replay mode, and the AOT smoke and surface checks.
- [ ] The name decision is recorded in `docs/planning/`.
  - No public type, namespace, package id or analyzer ID carries the vendor name "Jev". The only exception is a deliberate `ZeroAlloc.Jev` convenience or redirect package, if Phase 6.1 decides on one.
  - The repository and docs site have moved to the new name, or the maintainer's remaining actions are listed in STATE.md.
- [ ] The generator's output contains no `/v1/systemone` wire JSON; serialization lives only in protocol adapters. A test serializes one question set for both protocols.
- [ ] `IDecisionClient` is the client abstraction, with retries, telemetry and logging as pipeline stages. Every allocation budget on the `/v1/systemone` path is unchanged or tightened.
- [ ] Both adapters have recorded-fixture tests. An unsupported question kind, or image state on a provider without image input, fails with a typed error naming the provider and the question, never as a raw HTTP failure.
- [ ] Live runs pass against TypeSafe, OpenRouter and OpenAI, and against one local `/v1/systemone` server, either in CI or documented as run.
- [ ] The conformance suite runs green against recorded TypeSafe and OpenAI fixtures.
- [ ] A Providers docs page shows the presets and the capability matrix. A sample switches providers by configuration only.

No release-tag line: `docs/planning/CONVENTIONS.md` says "Milestone completion tags a release: no".

## Phases

1. **Phase 6.1: Name decision and rename** — `Surface: Refactor`
   - **Goal:** Decide the vendor-neutral name, the provider-package layout and the fate of `ZeroAlloc.Jev`. Then rename packages, namespaces, attributes, `IJevClient`/`JevError`, analyzer IDs, docs, samples, the repository and the docs site, with no behaviour change (#120).
2. **Phase 6.2: Neutral question model and adapter boundary** — `Surface: Refactor`
   - **Goal:** The generator emits a provider-neutral question-set description, and `/v1/systemone` serialization moves behind a protocol-adapter boundary, with no behaviour change (#115).
3. **Phase 6.3: IDecisionClient abstraction and pipeline** — `Surface: Backend`
   - **Goal:** An `IDecisionClient` that follows Microsoft.Extensions.AI conventions, with a builder pipeline in which retries, telemetry and logging are stages (core of #119).
4. **Phase 6.4: /v1/systemone adapter** — `Surface: Backend`
   - **Goal:**
     - presets for TypeSafe, OpenRouter, Cloudflare Clef, vLLM-SR and local servers;
     - capability flags with typed errors;
     - optional auth;
     - keyed DI clients;
     - a local server for tests (#115).
5. **Phase 6.5: OpenAI /v1/decisions adapter and image state** — `Surface: Backend`
   - **Goal:** An adapter for OpenAI's Decisions API, its schema and question mapping verified against OpenAI's docs first, plus typed state that carries images where a provider supports them (#115).
6. **Phase 6.6: Conformance suite** — `Surface: Infra`
   - **Goal:** Check any endpoint's shapes, multi-question requests, distributions, error shapes and model reporting, green against recorded TypeSafe and OpenAI fixtures (#116, conformance part).
7. **Phase 6.7: Providers docs and samples** — `Surface: Docs`
   - **Goal:** A Providers page with the presets and the capability matrix, and a sample that runs one question type against three providers by configuration only.

**Ordering:**
- The rename comes first, so new code is never written against names it would lose.
- 6.2 and 6.3 come before the adapters, because the adapters serialize the neutral model and implement the client.
- Conformance and docs close the milestone.

## Dependencies on Prior Milestones

- **Milestones 1–4:**
  - the generator, analyzers, typed results, client, retries, telemetry, logging and DI;
  - the docs site;
  - the samples, with replayed recordings.
- **Milestone 5:**
  - the reviewed API, which is the starting point for the rename;
  - the AOT surface and entry-point coverage checks, which gate every refactor;
  - the allocation budgets and the measurement fix;
  - the live harness, with both keys in `live-api`;
  - the real TypeSafe and OpenRouter error bodies.
- The Phase 5.5 publishing pipeline waits on `phase/5.5-release` for Milestone 7. This milestone does not touch it.

## External Constraints

- **Maintainer actions:**
  - renaming the GitHub repository;
  - transferring the repository to `MarcelRoozekrans/Minos.NET` (decided in Phase 6.1: the project leaves the ZeroAlloc org and is renamed Minos);
  - enabling GitHub Pages and re-creating the org-provided secrets and ruleset on the personal repository;
  - removing the org's docs app and redirecting jev.zeroalloc.net;
  - providing an OpenAI API key in `live-api` for Phase 6.5's live runs.
- **OpenAI's Decisions API** has been in public beta since 2026-10-06, so its schema may change.
- **The local `/v1/systemone` server** used in tests, chosen in Phase 6.4, must be runnable on GitHub-hosted runners or documented as a manual run. Its licence must allow it.

## Risk Areas

| Risk | Impact | Mitigation |
|---|---|---|
| The rename's churn hides a regression | A behaviour change slips in during a mechanical change | 6.1 changes names only. The full suite, AOT surface, entry-point coverage and docs tests gate it. Behaviour work starts in 6.2. |
| The neutral model costs allocations | Budgets loosen | 6.2 and 6.3 keep every `/v1/systemone` budget unchanged or tightened, measured with the Phase 5.3 method. |
| OpenAI's schema differs from what was assumed | Adapter rework | 6.5 verifies the docs and records fixtures before building, and treats beta changes as fixture updates. |
| The milestone is large (7 phases) | A long road to 1.0 | 6.6 and 6.7 can split into their own milestone without changing the order of the API-shaping phases. |
| The repository or docs-site move breaks links | Lost docs URL, broken badges | 6.1 plans redirects: GitHub keeps the old repository URL redirecting, and the docs domain gets a redirect. |

## Open Questions

None at milestone level. These are decided inside their phases:
- **6.1:** the name, provider-package granularity, and the fate of `ZeroAlloc.Jev`.
- **6.3:** whether `IDecisionClient` lives in the core or in an `.Extensions.AI` package.
- **6.4:** the local test server.
