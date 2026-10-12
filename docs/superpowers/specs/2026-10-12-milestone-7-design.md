# Milestone 7 Design — `1.0 release`

**Date:** 2026-10-12
**Milestone:** `7`
**Stage:** milestone (one milestone, multiple phases)

> Follows the roadmap revision in `docs/superpowers/specs/2026-10-09-roadmap-design.md` and the Milestone 6 audit, `docs/plans/2026-10-12-milestone-6-audit.md`. Issues: #143 (pipeline stages), #104 (async allocation gate), #145 (pack tests hang), #51 (Roslyn workarounds), #29 (NuGet publishing), #28 (api-compat), #161 (OpenAI fixtures, out of scope).

## Goal

Ship Minos.NET 1.0.0 on NuGet with an API worth freezing.
1. Add the two pipeline stages that were left out, caching and rate limiting (#143).
2. Review the whole public surface once.
3. Re-run the live, AOT and allocation checks on the final API.
4. Publish every package as 1.0.0 through release-please and NuGet Trusted Publishing, with api-compat guarding each later change.

The OpenAI adapter ships as 1.0.0 too. Its docs and package description say that it is built from OpenAI's Decisions guide and has never been tested against the live API.

## Decisions

- **OpenAI is not verified in this milestone.** The maintainer has no OpenAI account. The live OpenAI run and the recorded OpenAI fixtures, which moved here from Milestone 6, are removed from this milestone's Definition of Done. #161 stays open and is relabelled as needing an OpenAI account.
- **`Minos.NET.OpenAI` ships as 1.0.0, marked unverified.** All packages version together. Rejected options:
  - a prerelease version of the OpenAI package;
  - not publishing it.
- **#143 lands before the API review,** so the review and the freeze cover the new stages.
- **#51 is a check, not a requirement.** Both Roslyn defects are still open, dotnet/roslyn#85804 and #77119, and the analyzers pin Roslyn 5.0.0. The workarounds are removed only if Roslyn has fixed both defects and the pin can move to a fixed version. Otherwise #51 stays open.
- **#104 is unblocked.** ZeroAlloc.TestHelpers#65 shipped in v1.7.0 on 2026-10-12.

## Definition of Done

- [ ] All planned phases are complete.
- [ ] All tests pass: unit, generator, analyzer, DI, docs, integration, pack, samples in replay mode, and the AOT smoke and surface checks. The pack tests also run locally without hanging (#145).
- [ ] The client has caching and rate-limiting pipeline stages (#143), and both are documented. Core gains no new package dependency: if the cache needs one, it lives in an extension package.
- [ ] The whole public API, including the new stages, is reviewed, and `PublicAPI.Shipped.txt` describes 1.0.0.
- [ ] Live runs pass on the final API against TypeSafe, OpenRouter and a local `/v1/systemone` server.
- [ ] Wherever the OpenAI adapter is described, it is labelled unverified against the live API: the Providers page, its README and its NuGet description. A docs test pins the label.
- [ ] Native AOT and trim checks cover the whole public API with no warnings. Every allocation budget is unchanged or tightened. The yielding checks use TestHelpers 1.7.0's async API (#104).
- [ ] 1.0.0 of every package is on NuGet through release-please and Trusted Publishing (#29). The guide states the version, the site links to NuGet, and api-compat guards later changes (#28).

No release-tag line: `docs/planning/CONVENTIONS.md` says "Milestone completion tags a release: no".

## Phases

1. **Phase 7.1: Caching and rate-limiting stages** — `Surface: Backend`
   - **Goal:** `UseCaching` and `UseRateLimiting` pipeline stages (#143).
     - Rate limiting wraps `System.Threading.RateLimiting` and fails with a `RateLimited` error before a request is sent.
     - Caching keys on the question definition, state and model.
   - The phase spec decides two things:
     - the cache's storage, likely `HybridCache` in an extension package;
     - how a cache hit appears in usage and telemetry.
2. **Phase 7.2: Public API review of the whole surface** — `Surface: Refactor`
   - **Goal:** Review sealing, naming, nullability and XML docs across every package, the new stages included.
   - The phase also:
     - adds the OpenAI "unverified" label with its docs test;
     - checks #51's Roslyn issues.
3. **Phase 7.3: Live verification on the final API** — `Surface: Infra`
   - **Goal:** Pass the live suites and the provider-switch sample against TypeSafe, OpenRouter and the local `/v1/systemone` server.
   - Each billed run needs the maintainer's approval.
4. **Phase 7.4: AOT, trim and benchmark re-verification** — `Surface: Infra`
   - **Goal:** Re-run the whole-surface AOT and trim check and the benchmarks. No allocation budget may be loosened.
   - Move the yielding allocation checks to TestHelpers 1.7.0's async API (#104). Re-measure over 30 or more runs before setting any tolerance.
5. **Phase 7.5: Publish 1.0** — `Surface: Infra`
   - **Goal:** Publish 1.0.0 the way Thalos.NET, Rag.NET and AdoNet.Async do: release-please, and NuGet Trusted Publishing from `ci.yml`'s `publish-nuget` job, rehearsed against a local feed first.
   - Reuse what still applies from the paused `phase/5.5-release` branch, such as the package inspection.
   - Fix the pack-test hang (#145).
   - Turn on api-compat (#29, #28).
   - Add the NuGet links to the docs site's navbar and footer.

## Dependencies on Prior Milestones

- Depends on Milestone 6's provider-neutral core: the name, `IDecisionClient`, both adapters, the conformance suite and the Providers page. All complete on 2026-10-12.
- Reuses the reviewed pipeline work on the local branch `phase/5.5-release`, from the old Phase 5.5.

## External Constraints

- **nuget.org setup by the maintainer, before Phase 7.5 publishes:**
  - a Trusted Publishing policy for `MarcelRoozekrans/Minos.NET`;
  - confirmation that the `Minos.NET*` package ids are still free.

  Phase 7.5 writes out the exact steps.
- **Billed live runs:** TypeSafe and OpenRouter runs in Phase 7.3 need the maintainer's approval each time.
- **Roslyn:** #51 depends on dotnet/roslyn#85804 and #77119.

## Risk Areas

| Risk | Impact | Mitigation |
|---|---|---|
| A `Minos.NET` package id is taken on nuget.org before the publish | The packages cannot be published under their names | Check the ids at the start of Phase 7.5. If one is taken, make a rename decision before anything is published. |
| The caching design grows, for example over key stability or streaming | Phase 7.1 delays the release | Keep 7.1 to the two stages #143 describes. Further work becomes a 1.x issue, because stages are additive. |
| The API review breaks the samples or the docs | Rework late in the milestone | Phase 7.2 runs the full suite, the sample replays and `dotnet mdsnippets`. The 0.x versions allow breaks until 7.5. |
| A first publish cannot be undone | A wrong package is permanent on nuget.org | Rehearse against a local feed, and publish only with the maintainer's explicit go-ahead. |

## Open Questions

None.
