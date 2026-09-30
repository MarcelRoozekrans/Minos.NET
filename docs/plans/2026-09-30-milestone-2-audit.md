# Milestone 2 Audit — Typed .NET API

**Date:** 2026-09-30
**Verdict:** PASS, with three notes

| Criterion | Result | Evidence |
|---|---|---|
| All planned phases complete | PASS | Phases 2.1–2.4 are complete in ROADMAP.md and MILESTONE.md. Phase 2.4 merged as PR #62 (`78874a2`), and its follow-up fix as PR #64 (`3a43c88`). |
| All tests passing: unit, generator, analyzer, integration and pack | PASS | `dotnet test ZeroAlloc.Jev.slnx -c Release` on `main` at `3a43c88`: unit 526, generator 226, analyzers 196, integration 20 and pack 7 pass. CI, Benchmarks and Release Please are green on `3a43c88`. |
| Live smoke runs when `JEV_LIVE=1` and a key are set | PASS, with a note | The 8 live tests compile and skip without a key. The TypeSafe live suite has still not run, because no TypeSafe key is set locally or in the `live-api` environment. That includes Phase 2.4's `BuiltQuestionSet_ParsesAKeyedChoice`. |
| `[JevQuestions]` types evaluate end to end through `EvaluateAsync<T>` to `Result<T, JevError>`, with no reflection, plus raw `JsonElement`, string and UTF-8 overloads | PASS | Phase 2.1. The AOT smoke app exercises typed evaluation and its default-interface-method fallback under Native AOT. |
| Every Jev diagnostic comes from `ZeroAlloc.Jev.Analyzers`; the `[Criteria]` code fix ships in `ZeroAlloc.Jev.CodeFixes`; #4–#11 are closed | PASS | Phase 2.2. #4–#11 are closed, and the pack tests assert the analyzer and code-fix layout. |
| Structured instructions and criteria work in attributes and in builders | PASS | Attributes in Phase 2.3. Builders in Phase 2.4, via `JevCriterion` and `JevContent` instructions. A differential test proves that the builder and the generator send byte-identical questions. |
| Fluent builders cover Noul, Choice and Score, and validate the API limits at runtime with ZeroAlloc.Validation | PASS | Phase 2.4 covers enum and keyed Choice and Score. The rules are checked with ZeroAlloc.Validation 2.0.3, which fixes ZeroAlloc.Validation#282, and the limits come from `JevLimits`, shared with the analyzers. |
| Every phase adds `AllocationGate` budgets and benchmarks for what it ships; the AOT smoke app exercises the new typed paths; #13 is closed | PASS | 2.1 has the typed round-trip gate and typed benchmarks. 2.3 has the content-factory gates and `ContentBenchmarks`. 2.4 has gates for Build, evaluating a built set, parsing a built set, and `JevAnswers.Get` at 0 B, plus `QuestionSetBenchmarks`. 2.2 ships only analyzers and code fixes, so it has no runtime path to budget. #13 is closed. |
| #12 and #22 are closed | PASS | Both were closed in Phase 2.1. |
| Pre-push reviews on file | Note | There are no `docs/pre-push-review-*.md` reports. Every phase instead ran a review after each task, with fix rounds, and a final whole-branch review. |
| The release will tag correctly | Skipped | CONVENTIONS sets `Milestone completion tags a release: no`, because release-please owns releases. Release PR #63 (0.2.0) lists Phase 2.4's features and the #61 fix. |

## Decisions recorded during the milestone
- **Transitive analyzers.** NuGet flows ZeroAlloc.Validation's analyzers to consumers regardless of the dependency's exclude flags (NuGet/Home#6720). They stay inert without `[Validate]` types, and the pack tests check that. Recorded in the phase 2.4 spec's Risks table.
- **Enum options.** Enum options in built question sets are read from the enum's public fields in declaration order, under `DynamicallyAccessedMembers`. This is a maintainer exception to the milestone's no-reflection goal: `Enum.GetName` names an aliased value by its alias in larger enums. Native AOT is verified by the smoke app.
- **Score legend.** The Score answer's `legend` is required, as the TypeSafe API specifies (#61, fixed in #64).

## Follow-ups carried forward
- **Live suite.** Run the TypeSafe live suite once a key is available, including `BuiltQuestionSet_ParsesAKeyedChoice`. This carries over from Milestone 1. Also record whether TypeSafe sends `Retry-After` and what the 422 body looks like.
- **Performance baseline.** `docs/performance.md`'s Baseline still says "To be recorded from the first full run". Record it from a full **Benchmarks** workflow run.
- **Release notes.** Phase PRs are squash-merged, so each phase PR needs a `BEGIN_COMMIT_OVERRIDE` block in its body; PR #62 needed one added after the merge. STATE.md's phase 1.6 advice to give phase PRs a plain title assumed merge commits.
- **Unchanged from Milestone 1.** #28 (api-compat) and #29 (NuGet publishing) still wait until the package is declared mature. #40 (`X-TypeSafe-Retry-Count`) is closed.
