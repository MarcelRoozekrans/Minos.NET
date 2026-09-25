# Roadmap change — `[JevQuestions]` source generator

**Date:** 2026-09-25
**Stage:** roadmap (reshapes Milestone 1 and Milestone 2; milestones 3–5 unchanged)
**Amends:** `docs/superpowers/specs/2026-09-24-roadmap-design.md`

## Idea

Declare a set of Jev questions as a C# type. A source generator emits the request's `questions` JSON at compile time and an allocation-free parser for the answers; analyzers enforce the API limits at compile time.

```csharp
[JevQuestions(State = typeof(TicketState))]
public partial record TicketTriage
{
    [Noul("Does `message` ask the recipient to disclose a sensitive credential?",
          True = "Asks for a password, API key, or MFA code",
          False = "No credential is requested")]
    public partial NoulAnswer RequestsCredentials { get; }

    [Choice("Which team should handle `message`?")]
    public partial ChoiceAnswer<Team> Team { get; }

    [Score("How urgent is `message`?")]
    public partial ScoreAnswer<Urgency> Urgency { get; }
}

public enum Team
{
    [Criteria("Charges, invoices, refunds", NotFor = "Login or access problems",
              Examples = ["I was charged twice", "Where is my invoice?"])]
    Billing,
    [Criteria("Login, profile, permissions, or security")] Account,
    [NoneOfTheAbove("No listed team fits")] Other,
}

public enum Urgency { [Level("Can wait")] Low, [Level("This week")] Medium, [Level("Today")] High }
```

The generator implements `IJevQuestionSet<TSelf>` on each `[JevQuestions]` type: a static `QuestionsUtf8` u8 literal holding the complete `questions` object, and a static `Parse(ref Utf8JsonReader)` that fills the partial properties. Answer names shown above (`NoulAnswer`, `ChoiceAnswer<T>`, …) are illustrative — see Deferred to phase brainstorms.

Planned diagnostics: JEV001 Choice enum empty or over the option limit; JEV002 Score enum outside 2–10 levels; JEV003 empty instructions; JEV004 instructions reference a field absent from the declared state type; JEV005 snake_case wire-key collision; JEV006 more than one `[NoneOfTheAbove]`. Code fix: generate `[Criteria]` stubs for bare enum members.

## Decisions

1. **Timing — the generator core moves into Milestone 1 now.** It depends only on the Phase 1.2 wire model, not on transport, so it proceeds while Transport waits on ZeroAlloc.Rest #298–#301. `EvaluateAsync<T>` and analyzers stay in Milestone 2.
2. **Fluent builders stay, alongside the generator.** The generator covers compile-time question sets; a thin fluent builder covers runtime-defined questions (options from a database, etc.). Both produce the same typed answer types.
3. **State input — typed state plus raw forms, never `object`.** `[JevQuestions(State = typeof(TicketState))]` makes `EvaluateAsync` take a `TicketState` serialized through a caller-supplied `JsonTypeInfo`, which also gives JEV004 its field list. Overloads for `JsonElement`, `string` and UTF-8 bytes remain. An `object`/anonymous-type parameter is rejected: STJ can only serialize it via reflection, which breaks the M1 zero-IL-warning DoD.
4. **Return type is `Result<T, JevError>`**, matching the Milestone 1 client contract — not a bare `T`.
5. **Packaging — the generator ships inside the `Jev.Net` nupkg** under `analyzers/dotnet/cs`; no separate package. The generator project targets `netstandard2.0` as Roslyn requires and is not itself packable.

## Roadmap changes

### Milestone 1 — insert Phase 1.3, renumber the rest

**New Phase 1.3: Question generator core** — `Surface: Backend`
Incremental `[JevQuestions]` source generator with its attribute and runtime types (`[Noul]`, `[Choice]`, `[Score]`, `[Criteria]`, `[Level]`, `[NoneOfTheAbove]`, `IJevQuestionSet<TSelf>`, typed answer types), emitting `QuestionsUtf8` and a `Utf8JsonReader` answer parser. Emitted JSON is verified against the Phase 1.2 wire model and fixtures, with generator snapshot tests. No transport, no `EvaluateAsync`, no analyzers.

Renumbered: 1.4 Transport, 1.5 Error model, 1.6 Resilience, 1.7 Test harness, 1.8 CI and release pipeline. Phase 1.8 additionally verifies the packed nupkg contains the generator under `analyzers/dotnet/cs`.

Added to the M1 Definition of Done: *`[JevQuestions]` generator emits question JSON and parses answers, verified against the wire fixtures.*

### Milestone 2 — Typed .NET API, reshaped

| Phase | Goal | Surface | Replaces |
|---|---|---|---|
| 2.1 Typed evaluation | `EvaluateAsync<T>` returning `Result<T, JevError>`; typed state via `State = typeof(...)` + `JsonTypeInfo`; raw `JsonElement` / string / UTF-8 overloads | Backend | old 2.2 |
| 2.2 Analyzers and code fixes | JEV001–JEV006 including the JEV004 state-field check, and the `[Criteria]`-stub code fix | Backend | most of old 2.4 |
| 2.3 Structured instructions and criteria | `Examples` / `NotFor` in attributes, object/array instructions, `state` helpers | Backend | old 2.3 |
| 2.4 Fluent question builders | Runtime-defined questions sharing the typed answer types; runtime API-limit validation via ZeroAlloc.Validation (analyzers cannot see these) | Backend | old 2.1 + rest of old 2.4 |
| 2.5 Typed-layer performance | Allocation budgets and benchmarks for the generated parser and the builders | Backend | old 2.5 |

Milestone 2 Definition of Done:
- `[JevQuestions]` types evaluate end-to-end through `EvaluateAsync<T>` → `Result<T, JevError>`, with no reflection
- API limits enforced at compile time for generated question sets (JEV001–JEV006) and at runtime for builder-defined questions
- Fluent builders cover all three question types including structured criteria
- Typed layer stays within allocation budgets and the AOT smoke stays clean

### Ordering rationale

- **Generator core before Transport:** no dependency on transport, and Transport is blocked upstream — the phase uses otherwise idle time.
- **Generator core in M1, analyzers in M2:** M1 proves the emitted JSON and parser against the wire model; analyzers only pay off once developers write question sets against a working client.
- **2.1 before 2.2:** analyzers such as JEV004 depend on the `State = typeof(...)` shape settled in 2.1.
- **Builders after the generator:** they reuse the typed answer types the generator phase defines.

## Deferred to phase brainstorms

Resolved in the Phase 1.3 (and 2.x) brainstorms, not here:
- **Typed answer naming.** `NoulAnswer`, `ChoiceAnswer` and `ScoreAnswer` already exist as public wire classes in `Jev.Net`; the typed types need distinct names or a namespace.
- **Score → enum mapping.** The wire `score` is a `double` (expected level); `Value` needs a rule — rounding, or argmax of `probabilities`.
- **Probability storage.** `[InlineArray]` needs a constant length and cannot depend on `T`, and Choice allows up to 255 options — needs generator-emitted per-enum storage or another scheme. Enum ordinal ≠ enum value when members set explicit values.
- **Parser contract with the envelope.** Whether `Parse` reads the `answers` object only or the whole response, and how unknown or missing answer keys fail.

## Risks

| Risk | Impact | Mitigation |
|---|---|---|
| Generator and wire model drift apart | Emitted JSON the API rejects | Phase 1.3 tests round-trip emitted JSON through the 1.2 wire model and compare against fixtures |
| Two typed APIs (generator + builders) diverge | Inconsistent answers and validation | Both share the same typed answer types; builders land after the generator and reuse them |
| Generator packaging mistakes (analyzer missing from nupkg, wrong TFM) | Consumers get no generated code | Phase 1.8 verifies the nupkg layout; generator targets `netstandard2.0` |
| TypeSafe API changes shape | Generated code emits a stale schema | Live smoke suite (1.7) as early warning; generator emits only from wire-model-validated shapes |
