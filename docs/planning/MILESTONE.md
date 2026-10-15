# Milestone 7: 1.0 release

**Status:** active
**Started:** 2026-10-12
**Design:** `docs/superpowers/specs/2026-10-12-milestone-7-design.md`

## Goal
Ship Minos.NET 1.0.0 on NuGet with an API worth freezing.
1. Add the two pipeline stages that were left out, caching and rate limiting (#143).
2. Review the whole public surface once.
3. Re-run the live, AOT and allocation checks on the final API.
4. Publish every package as 1.0.0 through release-please and NuGet Trusted Publishing, with api-compat guarding each later change.

The OpenAI adapter ships as 1.0.0 too. Its docs and package description say that it is built from OpenAI's Decisions guide and has never been tested against the live API.

## Definition of Done
- [ ] All planned phases are complete.
- [ ] All tests pass: unit, generator, analyzer, DI, docs, integration, pack, samples in replay mode, and the AOT smoke and surface checks. The pack tests also run locally without hanging (#145).
- [ ] The client has caching and rate-limiting pipeline stages (#143), and both are documented. Core gains no new package dependency: if the cache needs one, it lives in an extension package.
- [x] The whole public API, including the new stages, is reviewed, and `PublicAPI.Shipped.txt` describes 1.0.0.
- [x] Live runs pass on the final API against TypeSafe, OpenRouter and a local `/v1/systemone` server.
- [x] Wherever the OpenAI adapter is described, it is labelled unverified against the live API: the Providers page, its README and its NuGet description. A docs test pins the label.
- [x] Native AOT and trim checks cover the whole public API with no warnings. Every allocation budget is unchanged or tightened. The yielding checks use TestHelpers 1.7.0's async API (#104).
- [x] 1.0.0 of every package is on NuGet through release-please and Trusted Publishing (#29). The first published version is 1.0.1: 1.0.0 was tagged, but nuget.org refused it over another owner's prefix reservation. The guide states the version, the site links to NuGet, and api-compat guards later changes (#28).

**Out of scope:**
- The live OpenAI run and the recorded OpenAI fixtures moved here from Milestone 6. They were removed on 2026-10-12, because the maintainer has no OpenAI account. #161 stays open.
- #51 was checked in Phase 7.2 on 2026-10-13. It stays open while dotnet/roslyn#85804 and #77119 are unfixed.

## Phases
1. Phase 7.1 — Caching and rate-limiting stages [complete]
2. Phase 7.2 — Public API review of the whole surface [complete]
3. Phase 7.3 — Live verification on the final API [complete]
4. Phase 7.4 — AOT, trim and benchmark re-verification [complete]
5. Phase 7.5 — Publish 1.0 [complete]

## Audit History
| Date | Verdict | Gaps |
|---|---|---|
