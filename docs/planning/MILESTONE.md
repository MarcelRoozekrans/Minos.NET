# Milestone 4: Patterns & docs

**Status:** complete
**Started:** 2026-10-02
**Completed:** 2026-10-03
**Design:** `docs/superpowers/specs/2026-10-02-milestone-4-design.md`

## Goal
A C# developer can learn Jev from its own docs site and apply TypeSafe's documented patterns in idiomatic C#. The library adds only thin, allocation-free helpers, and only where hand-written code tends to go wrong. Original, runnable samples show guardrails, intent routing and re-ranking end to end. CI keeps the samples honest by replaying recorded real answers.

## Definition of Done
- [x] All planned phases complete.
- [x] All tests passing: unit, generator, analyzer, integration and pack, plus every sample in replay mode. Live smoke still runs when `JEV_LIVE=1` and a key are set.
- [x] Pattern helpers ship in the core:
  - A normalized Score value, and a confidence-tier gate with overridable 0.5 and 0.9 defaults.
  - Both are in `PublicAPI.Unshipped.txt`, with 0 B `AllocationGate` budgets.
  - The AOT smoke app exercises them with zero IL2xxx/IL3xxx warnings.
- [x] Guides for all four documented patterns live in `docs/`: fan-out, confidence routing, composite scoring and intent routing. Their C# snippets compile in CI.
- [x] Three original samples are C# projects under `samples/`: guardrails, intent routing and re-ranking.
  - Each credits and links the TypeSafe cookbook it was inspired by.
  - Each runs live with a key, and in replay mode from checked-in recordings of a real run.
  - CI runs each one in replay mode and checks its decisions.
- [x] The user guide in `docs/` covers getting started, every question type, typed evaluation and builders, DI and configuration, logging and telemetry, Native AOT, and patterns and samples. #16 is closed.
- [x] jev.zeroalloc.net serves the guide through `apps/docs-jev` and `repos/jev` in ZeroAlloc-Net/.website. A push to `docs/` on `main` updates it through `trigger-website.yml`.
- [x] The README carries the unofficial-client disclaimer and the logo, and links the site.
- [x] Every existing allocation budget is unchanged.

## Phases
1. Phase 4.1 — Pattern helpers and guides [complete]
2. Phase 4.2 — Cookbook samples [complete]
3. Phase 4.3 — User guide [complete]
4. Phase 4.4 — Docs site, logo and README [complete]

## Audit History
| Date | Verdict | Gaps |
|---|---|---|
| 2026-10-03 | FAIL | jev.zeroalloc.net not served: Cloudflare project `za-docs-jev` and its domain not created yet (maintainer step); see `docs/plans/2026-10-03-milestone-4-audit.md` |
| 2026-10-03 | PASS | Re-audit after the Cloudflare setup: the site serves the guide. Note: .website PR #76 (submodule update with the shared icon) still open |
