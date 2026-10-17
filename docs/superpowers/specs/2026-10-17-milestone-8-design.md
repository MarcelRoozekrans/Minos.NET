# Milestone 8 Design — `Choosing a provider and a threshold`

**Date:** 2026-10-17
**Milestone:** `8`
**Stage:** milestone (one milestone, multiple phases)

> Follows the roadmap design `docs/superpowers/specs/2026-10-09-roadmap-design.md` and implements the calibration part of #116.
>
> The conformance part of #116 was built in Phase 6.6 as `Minos.NET.Conformance`, and is not packaged yet: its csproj has `IsPackable=false`. This milestone ships it inside the tool.
>
> Related:
> - #118, Milestone 9's decision export, will write the dataset format this milestone defines.
> - #124, recalibration and cost-aware rules, will reuse its metrics.

## Goal

A .NET user can measure, on their own labelled data, how accurate and how well calibrated each provider is. "Well calibrated" means a confidence of 0.9 really is right about 90 % of the time. They can then pick a provider and a confidence threshold from evidence, not vendor claims.

The milestone ships:
- a documented JSONL dataset format;
- a metrics library;
- a `minos` command-line tool that runs conformance and calibration against any configured provider;
- a guide backed by real reports. The reports compare TypeSafe's Jev, Cloudflare's Clef Flash through OpenRouter, and a local decision model, on a public sample dataset.

## Decisions

Made by the maintainer on 2026-10-17:

- **Sample dataset:** public and openly licensed. It is built from **Banking77**, the PolyAI test split: 3,080 real online-banking customer messages labelled with 77 intents, under CC BY 4.0.
  - **Choice question:** about 10 of the intents, picked so they are distinct and well represented.
  - **Noul questions:** derived from the human intent labels. For example, "Is this about a card that has not arrived?" is true exactly when the intent is `card_arrival`.
  - **Size:** about 300 to 500 items.
  - **Score:** Banking77 has no ordinal labels, so Score gets a small set written for this project and released as CC0. Its numbers are marked as weaker evidence.
  - **Attribution:** the dataset file and the guide credit PolyAI and the license.
- **Providers in the reports:**
  - TypeSafe's Jev;
  - Cloudflare's Clef Flash through OpenRouter. The maintainer's account allows it under zero data retention;
  - the local decision model that `local-systemone.yml` already runs, `Mapika/decider-2b-GGUF` on ollajev 0.4.0.
  - Luna is out, because zero data retention blocks it on this account. The Perplexity decider is out because it is unchecked.
- **The tool:** the package is `Minos.NET.Tool`, under the reserved prefix, and the command is `minos`, with `minos calibrate` and `minos conformance`.
- **What gets calibrated and swept is the number users gate on.**
  - Choice and Score use their `Confidence`, which `ConfidenceThresholds.Classify` already reads, so the guide's thresholds plug straight into the API.
  - Noul uses max(p, 1 − p) of its yes/no answer.
  - A Score answer is right only on an exact level match.
  - Option probabilities are not calibrated separately.
- **Cost comes from a dated price table.**
  - The tool takes per-token prices from configuration, and every report shows the prices it used and their date.
  - Jev uses the per-token price OpenRouter lists for TypeSafe's endpoint, so the maintainer does not have to look it up.
  - A provider that reports its own cost, such as OpenRouter, uses its figure.
  - The local model's cost is shown as 0, with a note.

Calls left open in the brainstorm and settled here, as my calls:

- **The metrics live in a library package, `Minos.NET.Evaluation`.** The tool is a thin shell around it. Milestone 9's shadow mode and #124's recalibration then reuse the metrics without depending on a command-line tool.
- **The provider configuration is the provider-switch sample's.** That is the `Minos:Active` and `Minos:Providers:<name>` sections, so one `appsettings.json` drives both the sample and the tool.
- **No recalibration or cost-aware decisions.** Those belong to #124. The reports measure and do not correct.
- **One request per item.** Each item's questions go in one request, the way an app sends a question set. Latency and cost are therefore per decision, meaning per request; accuracy and calibration are per question kind.
- **System One providers only.** The provider-switch configuration binds System One presets. OpenAI is unverified and there is no account, so it stays out. Adding it later means extending that configuration schema.
- **Conformance ships inside the tool.** `Minos.NET.Conformance` is bundled, not published as a seventh library, so it adds no public API, baseline or release entry.
- **The tool can override the model.** Clef Flash needs `Model: cloudflare/clef-flash`. The sample's `OpenRouter` section asks for `jev-latest`, so Clef gets its own section in the tool's configuration, with `ProviderPreferences.ZeroDataRetention`. The sample's own recordings are not changed.

## Definition of Done

- [ ] All planned phases are complete.
- [ ] All tests pass, including the new projects, the AOT checks and the docs tests.
- [ ] The JSONL dataset format is documented on the docs site, with a schema test. A loader reads it and reports line-numbered errors for bad items.
- [ ] `Minos.NET.Evaluation` computes, per question kind:
  - accuracy;
  - expected calibration error (ECE) and a reliability table;
  - a coverage and accuracy threshold sweep;
  - median and p95 latency per decision;
  - cost per 1,000 decisions.

  Each metric is checked against hand-computed values, and the results come out as Markdown and JSON.
- [ ] `Minos.NET.Tool` is installable with `dotnet tool install` and runs `minos calibrate` and `minos conformance` against any configured System One provider. It is published to nuget.org through the release path.
- [ ] Calibration reports for TypeSafe, Clef Flash and the local model on the Banking77 sample are committed. Each report records the date, the provider and model id, and the dataset version.
- [ ] A "Choosing a provider and a threshold" guide is published on the docs site and backed by those reports.

## Phases

1. **Phase 8.1: Dataset format, loader and the Banking77 sample** — `Surface: Data`
   - **Goal:** the JSONL format, one item per line, with the state, the questions and the expected answers keyed by question key. It is designed so #118's export can write it.
   - Also in this phase: a loader with clear errors, and the sample dataset built from Banking77 plus the authored Score set, with attribution and a reproducible build script.
2. **Phase 8.2: Metrics and reports in `Minos.NET.Evaluation`** — `Surface: Backend`
   - **Goal:** accuracy, ECE, the reliability table, the threshold sweep, latency and cost, from a run over a dataset through any `IDecisionClient`, rendered as Markdown and JSON.
3. **Phase 8.3: The `minos` tool** — `Surface: Infra`
   - **Goal:** `Minos.NET.Tool`, a dotnet tool with `calibrate` and `conformance` commands that read the provider-switch configuration, published to nuget.org through the release path.
4. **Phase 8.4: Reports and the guide** — `Surface: Docs`
   - **Goal:** run `minos calibrate` on the sample against the three providers, commit the reports, and write "Choosing a provider and a threshold" from them.
   - **Billing:** each hosted run is a billed live run that needs the maintainer's approval. The local run happens in CI.

## Dependencies on Prior Milestones

- **Milestone 6:**
  - `IDecisionClient`, the neutral `DecisionRequest`, and the provider presets the tool uses to call any provider;
  - `Minos.NET.Conformance`, which `minos conformance` wraps;
  - the provider-switch sample's configuration layout.
- **Milestone 7:** the release path and the reserved `Minos.NET` prefix on nuget.org. `Minos.NET.Tool` and `Minos.NET.Evaluation` publish through them.
- **Existing CI:** `local-systemone.yml` runs the local model for the local report.

## External Constraints

- **Billed live runs:** the hosted calibration runs, TypeSafe and Clef through OpenRouter, need the maintainer's approval each time.
  - The recorded OpenRouter Jev cost is about $0.000016 per request, for a short four-option Choice.
  - A 10-option Choice with Noul questions makes a bigger prompt, so a few hundred items cost a few cents per provider.
  - No Clef cost has been recorded yet.
- **Zero data retention:** the OpenRouter account is restricted to zero-data-retention endpoints. This rules out Luna, and limits Clef to the variants that are allowed.
- **Licensing:** Banking77 is CC BY 4.0, so the attribution must ship with the sample dataset and the guide.

## Risk Areas

| Risk | Impact | Mitigation |
|---|---|---|
| A few hundred items give noisy ECE, especially per question kind | Comparisons in the guide overstate differences | Report the item count and bin counts beside every metric. Use equal-mass bins. The guide states the uncertainty and does not rank providers on small differences. |
| Clef or the local model handles a 10-option Choice poorly, or refuses it | A gap in the comparison | That is a finding, recorded as such. Capability checks report an unsupported question set before any call. |
| Derived Noul questions are trivially easy given the intent | Inflated accuracy | Pick intents that are close to each other, such as the card-arrival, card-delivery and card-linking intents. Report the base rate beside accuracy. |
| Provider latency in CI is not representative | Misleading latency numbers | Record where the run happened. The guide treats latency as indicative only. |
| The format does not fit #118's export later | A breaking format change in Milestone 9 | Version the format from the start. Review the format against #118's recorder fields in Phase 8.1. |

## Open Questions

None.
