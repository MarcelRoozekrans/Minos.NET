# Milestone 4 Audit — Patterns & docs

**Date:** 2026-10-03
**Verdict:** PASS, with notes. The first pass failed only because jev.zeroalloc.net was not served yet. The maintainer then created the Cloudflare project and domain, and the re-audit passes.

| Criterion | Result | Evidence |
|---|---|---|
| All planned phases complete | PASS | Phases 4.1–4.4 are complete in ROADMAP.md and MILESTONE.md. They merged as PR #80, #82, #86 (`5326126`) and #88 (`a4676d6`), plus ZeroAlloc-Net/.website#81 (`366ef09`). |
| All tests passing: unit, generator, analyzer, integration, pack, and every sample in replay mode | PASS | `dotnet test -c Release` on `main` at `e1f65c0` passes every suite: unit 796, docs 341, generator 226, analyzers 196, samples 139, DI 42, integration 26, pack 15. CI, Docs site, Benchmarks and Release Please are green on `main`. CI's build job also runs every sample's real entry point in replay mode. |
| Live smoke runs when `JEV_LIVE=1` and a key are set | PASS, with a note | The 8 live tests compile and skip without a key. The TypeSafe live suite has still not run, because no TypeSafe key is available. This carries over from earlier milestones. |
| Pattern helpers ship in the core: a normalized Score value and a confidence-tier gate with overridable 0.5 and 0.9 defaults | PASS | Phase 4.1 shipped `Score<T>.Normalized` and `KeyedScore.Normalized`, `ConfidenceTier`, and `ConfidenceThresholds`, whose `default` means 0.5 and 0.9. |
| The helpers are in `PublicAPI.Unshipped.txt` with 0 B `AllocationGate` budgets | PASS, with a note | They were in `PublicAPI.Unshipped.txt` when Phase 4.1 merged. Release 0.3.0 then moved them to `PublicAPI.Shipped.txt`, as the release-tracking job does for every release, which is the correct state now. The `PatternHelpers` gate holds `Classify` and both `Normalized` properties to 0 B per call. |
| The AOT smoke app exercises the helpers with zero IL2xxx/IL3xxx warnings | PASS | `samples/ZeroAlloc.Jev.AotSmoke/AllocationChecks.cs` `PatternHelpers()` runs from `Program.cs`. The smoke app treats every warning as an error, and the `aot-smoke` job is green on `main`. |
| Guides for the four patterns live in `docs/`, with C# snippets that compile in CI | PASS | `docs/patterns/` holds fan-out, confidence routing, composite scoring and intent routing, plus an index. Every C# block is a MarkdownSnippets region in `tests/ZeroAlloc.Jev.Docs.Tests`, and CI fails on snippet drift. |
| Three original samples under `samples/`, each crediting its TypeSafe cookbook | PASS | Guardrails, IntentRouting and Reranking. Each README links the cookbook that inspired it and copies no text, code or data from it. |
| Each sample runs live with a key and in replay mode from checked-in recordings of a real run | PASS | Phase 4.2: `--live`, `--record` and `--replay`, the default. The answers were recorded on OpenRouter on 2026-10-02 with `typesafe/jev-1.13-20260917`. |
| CI runs each sample in replay mode and checks its decisions | PASS | `tests/ZeroAlloc.Jev.Samples.Tests` pins every decision and snapshots each report. CI also runs each `Program.cs` in replay mode. |
| The user guide covers every listed area, and #16 is closed | PASS | Phase 4.3 covers getting started, question types, typed evaluation, run-time question sets, client and errors, DI and configuration, observability, Native AOT, diagnostics, testing, patterns, samples and performance. #16 is closed. |
| jev.zeroalloc.net serves the guide through `apps/docs-jev` and `repos/jev`, and a push to `docs/` on `main` updates it through `trigger-website.yml` | PASS, with a note | Re-audited 2026-10-03, after the maintainer created the Cloudflare project and domain. https://jev.zeroalloc.net/, `/question-types`, `/patterns` and `/icon.png` answer 200. `trigger-website.yml` ran on `a4676d6`, and .website's update workflow moved `repos/jev` in its "update submodules" PR #76. **Note:** #76 was still open at the re-audit, so the site serves .website#81's pinned commit, with the guide but the old placeholder icon. Merging #76 brings Phase 4.4's commit and the shared icon. |
| The README carries the unofficial-client disclaimer and the logo, and links the site | PASS | Phase 4.4: the shared ZeroAlloc icon, the disclaimer verbatim, and one jev.zeroalloc.net link per guide page. Docs tests check every link against the site's routes and headings. |
| Every existing allocation budget is unchanged | PASS | Since Milestone 3 ended at `12ef375`, `samples/ZeroAlloc.Jev.AotSmoke` has 63 lines added and none removed. The only additions are the new `PatternHelpers` gate and its built set. |
| Pre-push reviews on file | Note | There are no `docs/pre-push-review-*.md` reports. Every phase ran a review after each task, with fix rounds, and a final whole-branch review on the most capable model. |
| The release will tag correctly | Skipped | CONVENTIONS sets `Milestone completion tags a release: no`, because release-please owns releases. Releases 0.3.0 to 0.3.3 carried the milestone. Release 0.3.3's changelog has only the PR title for Phase 4.4, because GitHub never associated the squash commit with #88, so release-please could not read the PR body's override. The docs entry is still credited. |

## The gap, now closed

The first pass failed because jev.zeroalloc.net did not resolve. The Cloudflare Workers project `za-docs-jev` and its custom domain had not been created yet. The maintainer created both on 2026-10-03, and the re-audit found the site serving the guide. One step remains: merge .website's "update submodules" PR #76, so the site serves Phase 4.4's commit with the shared icon.

## Decisions recorded during the milestone
- **The helpers stay thin (4.1).** There are two helpers. `ConfidenceThresholds` is a struct whose `default` means 0.5 and 0.9, and everything else stays a guide.
- **Our own example data (4.1).** The guides and samples use authored data. TypeSafe's example data has no known reuse terms.
- **Replay through the HTTP handler (4.2).** The whole client stack runs on recorded answers, and CI replays them offline.
- **The guide is the source of user documentation (4.3).** The README became an overview in 4.4.
- **Site choices (4.4).** Jev reuses the shared icon, is listed on the zeroalloc.net home page as available, and runs a `docs-site` CI job. The site app uses `onBrokenLinks: 'throw'`, unlike the sibling apps.

## Follow-ups carried forward
- **Site:** the Cloudflare steps above.
- **Live suite:** run the TypeSafe live suite once a key is available.
- **Performance baseline:** `docs/performance.md`'s Baseline still waits for a full Benchmarks run.
- **Open issues:**
  - #79: the relative AOT gates.
  - #85: adopt ZeroAlloc.Telemetry 1.11.0.
  - #73 and #74: the allocation-measuring API.
  - #68: budgets.
  - #67, #23, #24 and #25: Phase 5.1.
  - #28 and #29: api-compat and NuGet publishing. #29 now notes the README Status line.
