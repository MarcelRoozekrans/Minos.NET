# Roadmap Design — `ZeroAlloc.Jev → a provider-neutral decision client` (revision, 2026-10-09)

**Date:** 2026-10-09
**Author:** Claude, with the maintainer
**Stage:** roadmap (whole project, from Milestone 5 onward)

> This revises `docs/superpowers/specs/2026-09-24-roadmap-design.md`. Milestones 1–4 are complete and stand.
> The maintainer's reasons and the vendor landscape are recorded in `docs/plans/2026-10-09-provider-neutral-direction.md`.
> The work is tracked in issues #115–#119 and a new name issue.

## Goal

Make this library the typed, provider-neutral decision client for .NET. One set of source-generated, allocation-conscious, Native AOT-compatible typed questions (Noul, Choice, Score) should run against any decision-model provider by configuration alone:
- TypeSafe Jev, OpenRouter, Cloudflare Clef and open models that speak `/v1/systemone`;
- OpenAI's `/v1/decisions`;
- later, an in-process local model.

On top of that core, the library adds what no single vendor offers neutrally:
- measuring each provider's calibration on the user's own data;
- moving decisions from hosted to cheaper or local models;
- escalating uncertain decisions.

1.0 ships under a vendor-neutral name, once the API that carries this design is in place.

## Target Users / Stakeholders

- **.NET developers who need fast, typed, calibrated decisions:** routing, triage, guardrails, re-ranking. They want one API that does not tie them to a vendor, works under Native AOT, and stays allocation-conscious.
- **Teams that move high-volume decisions from a hosted model to a cheaper or local one.** They need logging, export, shadow comparison and calibration evidence.
- **The ZeroAlloc-Net org and its maintainer,** who need consistent packaging, publishing and docs with the sibling libraries.
- **Out of scope:** training or fine-tuning models, generative chat (`IChatClient` already covers that), and non-.NET clients.

## Top-Level Success Criteria

- The same `[…Questions]` type runs against TypeSafe, OpenAI and a local `/v1/systemone` server by configuration only. Live runs prove each one.
- 1.0 is published on NuGet under a vendor-neutral name. Its public API is reviewed, guarded by api-compat, verified under Native AOT, and holds the existing allocation budgets.
- A dotnet tool reports conformance and calibration per provider on a labeled dataset, and the docs use it to recommend providers and thresholds.
- A documented path moves decisions from hosted to local: record, export, shadow-compare, switch. It includes an in-process provider, if the spike says go.
- Uncertain decisions can escalate through a provider chain, behind an `IDecisionClient` that follows Microsoft.Extensions.AI conventions.

## Milestones

### Milestone 5: 1.0 hardening (re-scoped, closes now)

**Goal:** Harden the current client: API review, benchmarks, AOT/trim verification, and live and alias verification. These are complete. The 1.0 publish moves to Milestone 7.

**Phases:**

1. Public API review — `Surface: Refactor` (complete)
2. Benchmark suite — `Surface: Backend` (complete)
3. AOT, trim and measurement verification — `Surface: Infra` (complete)
4. Live and alias verification — `Surface: Infra` (complete)

The old Phase 5.5, "1.0 release", leaves this milestone. Its reviewed pipeline work on `phase/5.5-release` is reused in Milestone 7.

### Milestone 6: Provider-neutral core

**Goal:** Rename to a vendor-neutral name and reshape the core around a neutral question model, `IDecisionClient` and two protocol adapters, validated by a conformance suite. This happens before any 1.0 API freeze.

**Phases:**

1. Name decision and rename — `Surface: Refactor`
2. Neutral question model and adapter boundary — `Surface: Refactor`
3. `IDecisionClient` abstraction and pipeline — `Surface: Backend`
4. `/v1/systemone` adapter: presets, capability flags, optional auth, keyed DI — `Surface: Backend`
5. OpenAI `/v1/decisions` adapter and image state — `Surface: Backend`
6. Conformance suite — `Surface: Infra`
7. Providers docs and samples — `Surface: Docs`

### Milestone 7: 1.0 release

**Goal:** Review, verify and publish the provider-neutral library as 1.0.0 on NuGet, with api-compat guarding every later change.

**Phases:**

1. Public API review of the renamed surface — `Surface: Refactor`
2. Live verification across providers — `Surface: Infra`
3. AOT, trim and benchmark re-verification — `Surface: Infra`
4. Publish 1.0 — `Surface: Infra`

### Milestone 8: Choosing a provider and a threshold

**Goal:** A dotnet tool that measures accuracy, calibration, selective-prediction coverage, latency and cost per provider on a labeled dataset, and docs that use it.

**Phases:**

1. Labeled dataset format and loader — `Surface: Data`
2. Calibration metrics and report — `Surface: Backend`
3. Dotnet tool packaging, with conformance and calibration in one tool — `Surface: Infra`
4. "Choosing a provider and a threshold" guide — `Surface: Docs`

### Milestone 9: Hosted to local

**Goal:** Record decisions and outcomes, export them in an open format, shadow-compare a candidate provider, and serve decisions in-process from .NET if the spike says go.

**Phases:**

1. Decision recorder and outcome labels — `Surface: Backend`
2. JSONL export and redaction hooks — `Surface: Data`
3. Shadow mode and its report — `Surface: Backend`
4. Spike: in-process decision-model inference — `Surface: Backend`
5. Local provider package (only on a go) — `Surface: Backend`
6. "Moving decisions to a cheaper or local model" guide — `Surface: Docs`

### Milestone 10: Escalation

**Goal:** Fast decisions first, escalating on low confidence to another provider or an `IChatClient`, with a provider fallback chain, built as `IDecisionClient` middleware.

**Phases:**

1. Escalation middleware with per-question thresholds — `Surface: Backend`
2. Provider fallback chain — `Surface: Backend`
3. Ticket-triage sample with path metrics — `Surface: Mixed`
4. Confidence-routing guide as a framework feature, and a dotnet/extensions proposal — `Surface: Docs`

## Dependencies and Ordering Rationale

- **M5 closes now:** its hardening is done. Publishing `ZeroAlloc.Jev` 1.0.0 would freeze a vendor-named API that M6 replaces.
- **M6 → M7:** 1.0 freezes the public API, so everything that shapes it comes first:
  - the name;
  - the neutral model, since the generator's output changes;
  - `IDecisionClient`, since shipping `IJevClient` first would force a major version or two parallel clients;
  - the adapters, with their providers, capabilities and errors.
  - The conformance suite proves the adapters before release.
- **Within M6:**
  - the name comes first, because every later phase is written in its terms;
  - the neutral model comes before the adapters, because they serialize it;
  - `IDecisionClient` comes before the adapters, because they implement it;
  - conformance and docs come last.
- **M7 → M8:** the calibration tool is additive and can ship after 1.0 without breaking anything.
- **M8 → M9:** shadow mode reuses M8's metrics and report code, and the export uses M8's dataset format.
- **M9 → M10:** escalation composes the clients, thresholds and local provider built earlier.
- **Can run earlier:** M9's inference spike (9.4) is research with no API dependency. If the maintainer wants the go/no-go answer sooner, it can run alongside M6–M8.
- **External dependencies:**
  - OpenAI's Decisions API, a public beta whose schema may change;
  - Cloudflare Workers AI or Clef access;
  - open-model servers and their licences;
  - nuget.org, with the shared org key;
  - the ZeroAlloc-Net/.website docs site, which a rename moves;
  - the org's shared publish workflow (ZeroAlloc-Net/.github#49), which may replace Jev's own job.

## Risk Register

| Risk | Impact | Mitigation |
|---|---|---|
| Vendor facts are unverified: OpenAI's schema, Clef's compatibility, model licences, self-reported benchmark numbers | Adapters built on assumptions | Each adapter or spike phase verifies its vendor's docs and runs live or against recorded fixtures before building on them. Benchmark claims are treated as claims. |
| OpenAI's Decisions API is a beta and its schema may change | A broken adapter after 1.0 | Recorded fixtures and the conformance suite catch drift. The adapter is versioned with its own package, if the name decision splits providers into packages. |
| Milestone 6 is large (7 phases) | A long gap before 1.0 | If it drags, split 6.6–6.7 into their own milestone. The order of the API-shaping phases stays. |
| A rename touches the repo, packages, analyzer IDs, docs site and samples | Broken links, a lost docs URL, confused users | Phase 6.1 plans redirects (GitHub repo rename, docs domain) and the fate of `ZeroAlloc.Jev`. Nothing is published under the old name, so no package is orphaned. |
| `IDecisionClient` follows M.E.AI conventions that may change | Rework | Mirror the stable `IChatClient` patterns. Propose upstream only once the API settles, in Milestone 10. |
| The in-process inference spike may say no-go | No local provider | It is a spike with an explicit go/no-go. Milestone 9's other phases stand without it. |

## Open Questions

None blocking this roadmap. These are decided inside their phases:
- **6.1:** the name, provider-package granularity, and the fate of `ZeroAlloc.Jev`.
- **6.3:** where `IDecisionClient` lives, in the core or an `.Extensions.AI` package.
- **9.4:** which model the spike targets.
