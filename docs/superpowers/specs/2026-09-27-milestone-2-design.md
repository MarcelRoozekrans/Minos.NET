# Milestone 2 Design — Typed .NET API

**Date:** 2026-09-27
**Milestone:** 2
**Stage:** milestone (one milestone, multiple phases)

> Input: `docs/superpowers/specs/2026-09-25-question-generator-roadmap-design.md`, the low-detail outline for this milestone. Brainstorm decisions, 2026-09-27:
> - performance work is folded into each phase instead of a separate phase 2.5;
> - all diagnostics move to one analyzer project, with code fixes in their own assembly;
> - the builders validate with ZeroAlloc.Validation, as planned.

## Goal

Questions and answers become strongly typed, idiomatic C# with no reflection. A question set is declared as a `[JevQuestions]` type, or built fluently at runtime. It is evaluated through `EvaluateAsync<T>` returning `Result<T, JevError>`, stays Native AOT-clean, and stays within allocation budgets that CI enforces.

## Definition of Done

- [ ] All planned phases complete.
- [ ] All tests passing: unit, generator, analyzer, integration and pack. Live smoke runs when `JEV_LIVE=1` and a key are set.
- [ ] `[JevQuestions]` types evaluate end to end through `EvaluateAsync<T>` → `Result<T, JevError>` with no reflection. Raw `JsonElement`, string and UTF-8 overloads exist too.
- [ ] Every Jev diagnostic comes from `ZeroAlloc.Jev.Analyzers`:
  - JEV001–004;
  - JEV101–107, moved out of the generator, which reports none.

  The `[Criteria]`-stub code fix ships in `ZeroAlloc.Jev.CodeFixes`. Issues #4–#11 are closed.
- [ ] Structured instructions and criteria (`Examples` / `NotFor`, object and array instructions, `state` helpers) work in attributes and in builders.
- [ ] Fluent builders cover Noul, Choice and Score, including structured criteria, and return the same typed answers as the generator. They validate the API limits at runtime with ZeroAlloc.Validation, using limits shared with the analyzers.
- [ ] Every phase adds `AllocationGate` budgets and benchmarks for what it ships. The AOT smoke app exercises the new typed paths with zero trim or AOT warnings. #13 is closed.
- [ ] #12 and #22 are closed.

## Phases

1. **Phase 2.1: Typed evaluation** — `Surface: Backend`
   - **Goal:** `EvaluateAsync<T>` returns `Result<T, JevError>`. It supports typed state via `[JevQuestions(State = typeof(...))]` with a caller-supplied `JsonTypeInfo`, plus raw `JsonElement`, string and UTF-8 overloads. The new member is added without breaking existing `IJevClient` implementations (#22). It also fixes record equality (#12) and ProbabilityMap boxing (#13), with budgets and benchmarks.
2. **Phase 2.2: Analyzers and code fixes** — `Surface: Backend`
   - **Goal:** `ZeroAlloc.Jev.Analyzers` hosts JEV001–004 and the JEV101–107 checks moved out of the generator (#4, fixing #5–#11 on the way). `ZeroAlloc.Jev.CodeFixes` adds the `[Criteria]` stub. Both ship under `analyzers/dotnet/cs`, and the pack tests assert them.
3. **Phase 2.3: Structured instructions and criteria** — `Surface: Backend`
   - **Goal:** `Examples` / `NotFor` in attributes are sent as a criterion object, which is a Jev.Net convention, not an API field. Object and array instructions and criteria are supported, along with `state` helpers, all across the attributes, the generator and the analyzers.
4. **Phase 2.4: Fluent question builders** — `Surface: Backend`
   - **Goal:** Runtime-defined Noul, Choice and Score questions share the generator's typed answer types. Runtime API-limit validation uses ZeroAlloc.Validation, and allocation budgets cover the builders.

## Dependencies on Prior Milestones

- **Milestone 1:** `JevClient`, the wire model, the `[JevQuestions]` generator core (`QuestionsUtf8`, `Parse`), the AOT smoke app with `AllocationGate`, the benchmarks project, and the pack tests.
- **ZeroAlloc packages:**
  - ZeroAlloc.Rest 2.2.0 and ZeroAlloc.Resilience 3.3.0, already adopted.
  - ZeroAlloc.Validation 2.0.1, for 2.4. Its only runtime dependency is ZeroAlloc.Pipeline.
- **Order:**
  - 2.1 comes before 2.2, because JEV004 depends on the `State` shape.
  - 2.2 comes before 2.3, because the new attribute checks live in the analyzer project.
  - 2.4 comes last, because it reuses the answer types, the limits and the structured criteria.

## Risks

| Risk | Impact | Mitigation |
|---|---|---|
| A new `IJevClient` member breaks existing implementations | Breaking change for implementers | Settle in 2.1: a default interface method, or an extension over the non-generic call (#22) |
| Moving JEV101–107 out of the generator changes when diagnostics appear | A diagnostic is lost or duplicated | Analyzer tests assert every existing diagnostic case; the generator tests assert the generator reports none |
| New analyzer and code-fix assemblies change the package layout | A consumer misses the analyzers, or the package bundles Workspaces | Extend the pack tests; the code-fix assembly's Workspaces reference stays private |
| ZeroAlloc.Validation is not AOT-clean in our use, or carries unused dependencies | `aot-smoke` fails, or a heavier package | Spike it at the start of 2.4; file upstream issues, as with Resilience #197 |
| Roslyn version drift across generator, analyzers and code fixes | IDE and SDK mismatch | Keep the single `Microsoft.CodeAnalysis.CSharp` 5.0.0 pin for all three; evaluate Renovate #37 against it |
| Per-phase budgets drift between win-x64 and linux-x64 | Flaky `aot-smoke` | The Phase 1.8 headroom rule: exact for layout-only allocations, about 10% for HTTP paths |
