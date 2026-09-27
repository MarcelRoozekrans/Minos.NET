# Milestone 2: Typed .NET API

**Status:** active
**Started:** 2026-09-27
**Design:** `docs/superpowers/specs/2026-09-27-milestone-2-design.md`

## Goal
Questions and answers become strongly typed, idiomatic C# with no reflection. A question set is declared as a `[JevQuestions]` type, or built fluently at runtime. It is evaluated through `EvaluateAsync<T>` returning `Result<T, JevError>`, stays Native AOT-clean, and stays within allocation budgets that CI enforces.

## Definition of Done
- [ ] All planned phases complete.
- [ ] All tests passing: unit, generator, analyzer, integration and pack. Live smoke runs when `JEV_LIVE=1` and a key are set.
- [ ] `[JevQuestions]` types evaluate end to end through `EvaluateAsync<T>` → `Result<T, JevError>` with no reflection. Raw `JsonElement`, string and UTF-8 overloads exist too.
- [ ] Every Jev diagnostic comes from `ZeroAlloc.Jev.Analyzers`: JEV001–004, and JEV101–107 moved out of the generator. The `[Criteria]`-stub code fix ships in `ZeroAlloc.Jev.CodeFixes`. #4–#11 are closed.
- [ ] Structured instructions and criteria work in attributes and in builders.
- [ ] Fluent builders cover Noul, Choice and Score, and validate the API limits at runtime with ZeroAlloc.Validation.
- [ ] Every phase adds `AllocationGate` budgets and benchmarks for what it ships. The AOT smoke app exercises the new typed paths. #13 is closed.
- [ ] #12 and #22 are closed.

## Phases
1. Phase 2.1 — Typed evaluation [active]
2. Phase 2.2 — Analyzers and code fixes [pending]
3. Phase 2.3 — Structured instructions and criteria [pending]
4. Phase 2.4 — Fluent question builders [pending]

## Audit History
| Date | Verdict | Gaps |
|---|---|---|
