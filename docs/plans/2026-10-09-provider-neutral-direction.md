# Proposed direction: a provider-neutral decision client (maintainer notes, 2026-10-09)

> Recorded verbatim from the maintainer during the Phase 5.5 pause, so the proposal survives the session. Status:
> **proposal, not yet decided or filed.** Item 1 blocks the first NuGet publish. Vendor facts and benchmark claims are
> as the maintainer wrote them on 2026-10-09, and are unverified here.

ZeroAlloc.Jev roadmap issues

Revised 2026-10-09 after OpenAI's Decisions API and Cloudflare's Clef launched. The decision-model category now has two wire formats and many vendors, so the library is moving from "a Jev client" to "a typed, provider-neutral decision client for .NET".

Landscape at time of writing:

TypeSafe Jev: hosted, POST /v1/systemone, text only.
OpenAI Decisions API: public beta since 2026-10-06, POST /v1/decisions, GPT-6 Luna only, text and image input. Calibration data has not been published yet.
Cloudflare Clef / Clef-flash: 27B / 9B, Apache-2.0, Jev-compatible, multimodal, hosted on Workers AI or self-hosted. Comes with a hosted RL fine-tuning service.
Open decision models speaking /v1/systemone: vLLM Semantic Router Decision 1.0/2.0, WaterSheep, LitJev, autotrust/JEV-27B, and others. Amazon's Strands Decider 2B has announced it will release its weights, training data and recipe.
Liquid AI and Perplexity also released decision models or APIs between 2026-09-29 and 2026-10-01.

Vendor benchmark numbers (speed, accuracy, calibration) are mostly self-reported. Treat them as claims, not facts.

## 1. Decide on a vendor-neutral package name before the first NuGet publish (blocking)

Context

"Jev" is one vendor's product name, and parts of the community are already calling it "dead". The library's value (source-generated typed questions, AOT, allocation budgets) is not tied to TypeSafe. With OpenAI and Cloudflare in the market, a Jev-branded package undersells it and will age badly.

Scope
- Pick an umbrella name (e.g. ZeroAlloc.Decisions) for the core: question attributes, generator, analyzers, result types and client abstraction.
- Provider packages underneath, e.g. ZeroAlloc.Decisions.TypeSafe, .OpenAI, .SystemOne (generic), .Local.
- Decide whether ZeroAlloc.Jev survives as a thin convenience package or redirect.
- Rename attributes and types where they carry the vendor name ([JevQuestions], IJevClient, JevError, the JEV analyzer IDs). Keep Noul / Choice / Score; those terms are shared across vendors.

Acceptance criteria
- [ ] Name decided and recorded in docs/planning/.
- [ ] Namespaces, packages, analyzer IDs and docs site renamed before the first NuGet publish.

## 2. One provider-neutral question model, two protocol adapters

Context

There are now two wire formats:

- /v1/systemone: TypeSafe, Clef, and most open models.
- /v1/decisions: OpenAI. It returns an answers array with each question's name echoed back, and supports image input. Dependent questions need separate requests, as with Jev.

The source generator should not emit a TypeSafe-shaped request. It should emit a neutral question set that adapters serialize.

Scope
- Generator emits a provider-neutral question-set description; serialization moves into protocol adapters.
- Adapter A: /v1/systemone, configurable base URL and model. Presets for TypeSafe, OpenRouter, Cloudflare Workers AI (Clef), vLLM-SR, and local servers.
- Adapter B: /v1/decisions (OpenAI). Verify the exact request and response schema and the question-type mapping (condition / fixed options / rubric -> Noul / Choice / Score) against the docs.
- Capability flags per provider: supported question kinds, image input, max questions per request, max context. Unsupported combinations fail at configuration time or as a typed error naming provider and question, never as a raw HTTP failure. (Some open servers support Noul only and return 422 for Choice and Score.)
- Typed state can include images where the provider supports it.
- Optional auth (local servers often need none).
- Keyed DI clients so an app can mix providers.
- Analyzer: warn when a question set uses a capability (e.g. image state) that a configured provider lacks, where this is statically knowable.

Acceptance criteria
- [ ] The same question type runs against TypeSafe, OpenAI and a local /v1/systemone server via configuration only.
- [ ] The capability matrix is documented in a "Providers" docs page.
- [ ] Recorded-fixture tests for both protocols (extend the WireMock setup).

## 3. Cross-provider conformance and calibration benchmark

Context

Competition is shifting from request shape to calibration, workflow accuracy and fine-tuning data. Calibration is the core of the product: a confidence threshold is only useful if 0.9 means about 90%. OpenAI has not published calibration data, and other vendors self-report. Nobody neutral measures this across providers, least of all on a user's own data.

Scope

Conformance part, per endpoint:
- request/response shape for Noul, Choice and Score
- multiple questions per request
- probability distributions sum to ~1; Score returns a level distribution plus expected value
- error shapes (422, 429 with request id, auth)
- model and version reporting

Calibration part, given a labeled dataset (JSONL of state, questions, ground truth):
- accuracy, expected calibration error (ECE) and reliability diagram per question kind
- coverage vs accuracy at confidence thresholds (selective prediction)
- latency (median, p95) and cost per 1k decisions

General requirements:
- Runs against any configured provider, so one dataset is compared across TypeSafe, OpenAI, Clef and local models.
- Ships as a dotnet tool that outputs markdown and JSON reports.

Acceptance criteria
- [ ] Conformance checks pass against recorded TypeSafe and OpenAI fixtures.
- [ ] Calibration report generated for at least two providers on a public sample dataset.
- [ ] Docs page "Choosing a provider and a threshold", backed by the tool.

## 4. Spike: in-process decision-model inference in .NET (no Python, Native AOT)

Context

All open decision models ship Python serving code. A decision model is prefill-only: one forward pass plus a decision head plus per-kind temperature calibration. That is much simpler to host than a generative model, and no .NET project offers it in-process.

Candidate models

Prefer small models with permissive licences:
- Amazon Strands Decider 2B: open weights, training data and recipe announced. Check the release status. The published recipe makes the head and calibration easier to reproduce.
- llm-semantic-router/Decision-1.0-Kai-0.6B
- joyfox/Qwen3.5-0.8B-JEV: Apache-2.0, query/key decision head, 1,024-token max input.
- Later or GPU-only: Clef-flash 9B (Apache-2.0, Jev-compatible).
- Reference for head and calibration math: autotrust/JEV-27B's published decision head and calibration files.

Spike goals
- Export backbone and decision head to ONNX (or reuse an existing export) and load with ONNX Runtime from .NET.
- Implement the decision head plus calibration in C#.
- Expose it as a provider (.Local) so existing typed question sets work unchanged.
- Measure: latency, allocations, Native AOT publish size, CPU vs GPU.

Out of scope

Training, quantisation tuning, and multimodal inputs.

Acceptance criteria
- [ ] One small model answers Noul, Choice and Score in-process, matching its reference Python output within tolerance on ~50 fixed cases.
- [ ] Go/no-go write-up in docs/planning/ with numbers and licence notes.
- [ ] The local provider can be measured by the calibration benchmark from issue 3.

## 5. Decision logging and vendor-neutral fine-tuning export

Context

The adoption path is: start hosted, then move high-volume decisions to a cheaper or local model. Cloudflare now sells this loop as a hosted service tied to its own platform. The value here is the same loop across every provider, exporting to an open format.

Scope
- Opt-in decision recorder (decorator on the client abstraction) that stores state, questions, answer + probabilities, provider, model version and request id.
- API to attach an outcome or ground-truth label later.
- JSONL export in the state / questions record shape that open decision models train on. The same format serves as the dataset input for issue 3.
- Shadow mode: run a candidate provider or model alongside the primary without acting on it. Report agreement, calibration and threshold sweep, reusing the issue 3 report code.
- Respect the existing "what it never records" observability rule: explicit opt-in, plus PII redaction hooks.

Acceptance criteria
- [ ] Recorder plus JSONL export with a documented schema.
- [ ] Shadow-mode report between two providers.
- [ ] Docs page "Moving decisions to a cheaper or local model".

## 6. System 1 / System 2 escalation + Microsoft.Extensions.AI decision abstraction

Context

The field is converging on "fast decision first, escalate to a reasoning model when unsure" (autotrust/JEV-27B, Celeris, OpenAI pairing Decisions with Responses). Microsoft.Extensions.AI has IChatClient and IEmbeddingGenerator but no decision primitive. With several vendors now in play, a neutral .NET abstraction is more valuable than before.

Scope
- IDecisionClient following M.E.AI conventions: builder pipeline with caching, OpenTelemetry, rate limiting and fallback, implemented by the provider adapters from issue 2.
- Escalation middleware: below a per-question threshold (sized to the cost of being wrong), fall back to another decision provider or an IChatClient with structured output. Record which path answered.
- Provider fallback chain, e.g. local -> Clef -> OpenAI.
- Once the API settles, open a discussion on dotnet/extensions proposing the abstraction.

Acceptance criteria
- [ ] Sample: ticket triage with local-first decisions, escalation on low confidence, and metrics showing the split.
- [ ] Composes with DI and keyed clients.
- [ ] Docs page turning the confidence-routing pattern into a framework feature.
