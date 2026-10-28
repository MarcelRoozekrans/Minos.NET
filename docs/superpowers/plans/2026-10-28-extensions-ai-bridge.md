# Minos.NET.Extensions.AI Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A new package, `Minos.NET.Extensions.AI`, that answers Minos question sets over any `IChatClient`, with every answer marked by how its probabilities were made.

**Architecture:** The core gains `ProbabilitySource`, carried per answer in `AnswerSlot` so that the generator, the typed answers, Caching and Recording all see it. The bridge sends one chat call per question, in parallel.
- With logprobs, it reads the top logprobs of the first output token from the OpenAI SDK's `ChatCompletion`.
- Without them, it asks for schema-bound JSON with each option's probability.

Conformance gains a chat-completions wire with multi-exchange fixtures.

**Tech Stack:** .NET 10, C#, xUnit 2.9.3. The packages are `Microsoft.Extensions.AI.Abstractions` 10.11.0, `Microsoft.Extensions.AI.OpenAI` 10.11.0 (tests and sample only) and `OpenAI` 2.14.0; check nuget.org for newer stable versions when adding them.

**Spec:** `docs/superpowers/specs/2026-10-28-extensions-ai-bridge-design.md`

## Global Constraints

- **Target:** `net10.0`, `TreatWarningsAsErrors`, and the analyzers as configured. Every public member goes into that project's `PublicAPI.Unshipped.txt`, because RS0016 and RS0017 are errors.
- **Native AOT:** zero IL2 and IL3 warnings, including from dependencies. No `#pragma`, `NoWarn` or `UnconditionalSuppressMessage` to get there; if a dependency warns, stop and report it.
- **Package and naming:**
  - The package is `Minos.NET.Extensions.AI` with namespace `Minos.Extensions.AI`.
  - The DI extensions live in namespace `Microsoft.Extensions.DependencyInjection`.
  - The core enum is `Minos.ProbabilitySource { Model = 0, Logprobs = 1, Verbalized = 2 }`. Its declaration order is the "weakest wins" order.
- **Fixed values from the spec:**
  - labels `A` to `Z`, with Noul `A` for yes and `B` for no;
  - `MaxChoiceOptions` default 26;
  - logprob call with `MaxOutputTokens = 1`, `Temperature = 0`, `TopLogProbabilityCount = 20`;
  - confidence is the picked option's probability, and a tie picks the lower index.
- **No secrets:** request state never goes into logs, traces or exception messages.
- **Commits:** conventional. The PR's squash title is `feat: add Minos.NET.Extensions.AI, an IDecisionClient over IChatClient`.
  - No nested parentheses in commit bodies.
  - No `Co-Authored-By`, Claude signature or session link.
- **Process:** builds and tests run one at a time, never in parallel, because the VM's memory is shared. Run a single test project with `dotnet test tests/<Project> --filter <Name>`. Run the whole solution only at the end of a task.
- **Branch:** `feat/extensions-ai-bridge`. Never push to `main`.

## Review Focus

1. **A model that answers ` A` or `a`.** ` A` counts for label `A` after trimming. `a` does not, because only exact uppercase counts. Pinned in Task 5.
2. **A verbalized reply with NaN, negative values, a missing label, or all zeros.** It returns `InvalidResponse` and is never re-asked a third time. Pinned in Task 7.
3. **The caller cancels while parallel calls run.** `OperationCanceledException` is thrown, never a `DecisionError`. One failing question cancels its siblings and returns that question's error. Pinned in Task 8.
4. **A Minos version older than this release reads a cache entry or record line that holds a `Verbalized` answer.** It misses or rejects it, and never reads it as calibrated. Old entries and lines read as `Model`. Pinned in Tasks 2 and 3.
5. **The bridge is disposed while its `IChatClient` is shared through DI.** The chat client stays usable. Pinned in Task 4.

---

### Task 1: Core `ProbabilitySource`, per answer

**Files:**
- Create: `src/Minos.NET/ProbabilitySource.cs`
- Modify:
  - `src/Minos.NET/AnswerSlot.cs`
  - `src/Minos.NET/QuestionAnswer.cs`
  - `src/Minos.NET/DecisionResponse.cs`, the public constructor at lines 52 and 69, plus `AnswerAt`
  - `src/Minos.NET/AnswerSlots.cs`, lines 43, 58 and 75
  - `src/Minos.NET/QuestionSetAnswers.cs`, lines 29, 41, 54, 65 and 76
  - `src/Minos.NET/Noul.cs`, `Choice.cs`, `Score.cs`, `KeyedChoice.cs` and `KeyedScore.cs`
  - `src/Minos.NET/PublicAPI.Unshipped.txt`
- Test: `tests/Minos.NET.Tests/ProbabilitySourceTests.cs`

**Interfaces:**
- Produces:
  - `public enum ProbabilitySource { Model = 0, Logprobs = 1, Verbalized = 2 }`
  - `internal readonly record struct AnswerSlot(int ValueIndex, double Value, double Confidence, int Offset, ProbabilitySource Source = ProbabilitySource.Model)`
  - `QuestionAnswer.ProbabilitySource { get; }`
  - new public overloads `QuestionAnswer.Noul(double probability, ProbabilitySource probabilitySource)`, `Choice(int chosenIndex, double confidence, double[] probabilities, ProbabilitySource probabilitySource)` and `Score(int level, double expected, double confidence, double[] probabilities, ProbabilitySource probabilitySource)`. The existing ones stay and mean `Model`.
  - `DecisionResponse.ProbabilitySource { get; }`, the highest enum value among its answers, or `Model` when there are none
  - `ProbabilitySource ProbabilitySource { get; }` on `Noul`, `Choice<T>`, `Score<T>`, `KeyedChoice` and `KeyedScore`, plus a public constructor `Noul(double probability, ProbabilitySource probabilitySource)`
  - `internal` constructors of the other typed answers gain a trailing `ProbabilitySource probabilitySource` parameter

- [ ] **Step 1: Write the failing tests** in `ProbabilitySourceTests.cs`:
  - `Factories_DefaultToModel`: `QuestionAnswer.Noul(0.7).ProbabilitySource == Model`, and likewise for Choice and Score.
  - `Response_CarriesEachAnswersSource`: a three-question definition with a Noul `Logprobs`, a Choice `Verbalized` and a Score `Model`. Each `response.Answers[i].ProbabilitySource` round-trips.
  - `Response_ReportsTheWeakestSource`: the same set gives `response.ProbabilitySource == Verbalized`. An all-`Logprobs` set gives `Logprobs`. A response built with the old constructor overloads gives `Model`.
  - `TypedAnswers_CarryTheSource`: evaluate a generated `[Questions]` record (reuse a test record from `GeneratedCreateTests.cs`) from a hand-built `DecisionResponse`. Assert `result.IsUrgent.ProbabilitySource`, `result.Team.ProbabilitySource` and the score's source. Do the same for the `QuestionSetAnswers` keyed path.
  - `Equality_IncludesTheSource`: `new Noul(0.7, Logprobs) != new Noul(0.7, Verbalized)`, and likewise for two `Choice<T>` that differ only in source.

- [ ] **Step 2: Run** `dotnet test tests/Minos.NET.Tests --filter ProbabilitySourceTests`. Expected: build fails, because `ProbabilitySource` is not defined.

- [ ] **Step 3: Implement** the interfaces above.
  - The source travels in `AnswerSlot.Source`. `DecisionResponse`'s public constructor copies `answer.ProbabilitySource` into the slot, and `AnswerAt` copies it back.
  - `AnswerSlots` and `QuestionSetAnswers` pass `slot.Source` to the typed constructors.
  - Equality and `GetHashCode` of the five typed answers include it.
  - Don't change the generator: it calls `AnswerSlots`, so its 13 snapshots must stay byte-identical.
  - Add the PublicAPI entries.

- [ ] **Step 4: Run** `dotnet test tests/Minos.NET.Tests`, then `dotnet test tests/Minos.NET.Generator.Tests`. Expected: all pass, and the generator snapshots are unchanged.

- [ ] **Step 5: Commit**: `feat: mark each answer with how its probabilities were made`

### Task 2: Caching carries the source

**Files:**
- Modify:
  - `src/Minos.NET.Caching/CachedDecision.cs`: `Version`, `From`, `ResponseFor`
  - `src/Minos.NET.Caching/CachedDecisionFormat.cs`: `Write`, `Fields`, `Read`
  - `src/Minos.NET/DecisionResponse.cs`: `AsCached` carries the slots unchanged; check it
- Test: `tests/Minos.NET.Caching.Tests/CachedDecisionFormatTests.cs`

**Interfaces:**
- Consumes: `AnswerSlot.Source` from Task 1.
- Produces:
  - `CachedDecision.Version` becomes `CurrentVersion = 2`, with `ModelOnlyVersion = 1`.
  - Wire: the top-level `"sources": [int, …]`, one per slot, written only when some slot is not `Model`. Such an entry is written with `"v": 2`. Otherwise the output is byte-identical to today's, with `"v": 1`.

- [ ] **Step 1: Write the failing tests:**
  - `ModelOnly_IsWrittenAsBefore`: an all-`Model` response serializes with `"v":1` and without `"sources"`. The existing `v == 1` assertion at line 44 stays green.
  - `MarkedAnswers_RoundTrip_AsVersion2`: a response with `Logprobs` and `Verbalized` answers writes `"v":2` and `"sources":[1,2]`. Reading it back gives the same per-answer sources and `IsCached`.
  - `Version1_WithoutSources_ReadsAsModel`: a v1 JSON literal from today's format reads with every source `Model`.
  - `Malformed_Sources_IsUnreadable` (theory):
    - a `sources` count that differs from the slot count;
    - a value outside 0 to 2;
    - a non-number;
    - `sources` present on a v1 entry.

- [ ] **Step 2: Run** `dotnet test tests/Minos.NET.Caching.Tests --filter CachedDecisionFormatTests`. Expected: the new tests fail.

- [ ] **Step 3: Implement** the format change. Keep `ReadSlots`' 4-element rule as it is.

- [ ] **Step 4: Run** `dotnet test tests/Minos.NET.Caching.Tests`. Expected: all pass.

- [ ] **Step 5: Commit**: `feat: the cache keeps each answer's probability source`

### Task 3: Recording carries the source

**Files:**
- Modify:
  - `src/Minos.NET.Recording/RecordedAnswer.cs`: the property, the constructor, `From`, equality
  - `src/Minos.NET.Recording/RecordFormat/RecordLine.cs`: `Version`, `WriteAnswer`
  - `src/Minos.NET.Recording/RecordFormat/RecordDtos.cs`: `AnswerDto`
  - `src/Minos.NET.Recording/RecordFileReader.cs`: lines 111-120, the version check
  - `src/Minos.NET.Recording/RecordFormat/RecordLineConverter.cs`: `TryToAnswer`
  - `src/Minos.NET.Recording/PublicAPI.Unshipped.txt`
- Test: `tests/Minos.NET.Recording.Tests/LineWriterTests.cs` and `RecordFileTests.cs`

**Interfaces:**
- Consumes: `QuestionAnswer.ProbabilitySource` from Task 1.
- Produces:
  - `RecordedAnswer.ProbabilitySource { get; }`. The internal constructor gains a trailing optional `ProbabilitySource probabilitySource = ProbabilitySource.Model`, so existing test calls compile unchanged.
  - Wire: the answer member `"probabilitySource": "logprobs" | "verbalized"`, written only when not `Model`. A line holding one has `"version": 2`.

- [ ] **Step 1: Write the failing tests:**
  - `MarkedAnswer_IsWrittenWithItsSource_AsVersion2`: a decision with one `Verbalized` Choice writes `"version":2` and `"probabilitySource":"verbalized"` on that answer.
  - `MarkedLine_RoundTrips`: the reader returns `ProbabilitySource.Verbalized` for that answer.
  - `Version1Line_ReadsAsModel`: an existing v1 line reads with `Model`.
  - `UnknownSource_IsRejected`: `"probabilitySource":"guessed"` makes the line unreadable, the same way the reader treats other bad lines.
  - The existing `TheLineFormat_IsPinned` snapshot stays unchanged.

- [ ] **Step 2: Run** `dotnet test tests/Minos.NET.Recording.Tests --filter "LineWriterTests|RecordFileTests"`. Expected: the new tests fail.

- [ ] **Step 3: Implement.** The reader accepts versions 1 and 2. `AnswerDto` gains `string? ProbabilitySource`, mapped in `TryToAnswer`. Equality includes the source.

- [ ] **Step 4: Run** `dotnet test tests/Minos.NET.Recording.Tests`. Expected: all pass, and the snapshot is unchanged.

- [ ] **Step 5: Commit**: `feat: decision records keep each answer's probability source`

### Task 4: Bridge package skeleton, AOT canary and release wiring

**Files:**
- Create:
  - `src/Minos.NET.Extensions.AI/Minos.NET.Extensions.AI.csproj`
  - `README.md`, `PublicAPI.Shipped.txt`, `PublicAPI.Unshipped.txt`
  - `ProbabilityMode.cs`, `ChatDecisionClientOptions.cs`, `ChatDecisionClient.cs`, `ChatClientDecisionExtensions.cs`
- Create: `tests/Minos.NET.Extensions.AI.Tests/` (csproj, `ScriptedChatClient.cs`, `ChatDecisionClientTests.cs`)
- Create:
  - `samples/Minos.NET.AotSmoke/ExtensionsAIChecks.cs`
  - `tests/Minos.NET.PackTests/ExtensionsAIPackageTests.cs`
- Modify:
  - `Directory.Packages.props`
  - `Minos.NET.slnx`
  - `src/Minos.NET/Minos.NET.csproj`, the `InternalsVisibleTo` entries at lines 44-58: add the package and its tests
  - `samples/Minos.NET.AotSmoke/Minos.NET.AotSmoke.csproj` and `Program.cs`
  - `samples/Minos.NET.AotSurface/Minos.NET.AotSurface.csproj`: the comment at line 4, `TrimmerRootAssembly` and `ProjectReference`
  - `samples/Minos.NET.AotSurface/SurfaceGenerics.cs`, if needed
  - the package lists in:
    - `tests/Minos.NET.AotSmoke.Tests/EntryPointCoverageTests.cs:13`
    - `PublicApi.cs:50`
    - `SurfaceGenericsTests.cs:21`
    - `SurfaceRuleTests.cs:17-26`
    - `tools/release/inspect_packages.py:20-31` and `tools/release/test_inspect_packages.py`
    - `.github/workflows/ci.yml:174-176, 199, 202, 264`
    - `RELEASING.md`: the package count goes from nine to ten `.nupkg`, and the line about new ids mentions `Minos.NET.Extensions.AI`

**Interfaces:**
- Consumes:
  - core internals through `InternalsVisibleTo`: `CapabilitySet(Kinds, MaxQuestions, MaxChoiceOptions, Images, ModelListing)`, with `.With(DecisionCapabilities?)`, `.Collect(definition, imageCount, providerName)`, `CapabilitySet.Unsupported(failures)` and `.ToOptions()`;
  - `ErrorBodies.KindOf(int status)`.
- Produces:
  - `public enum ProbabilityMode { Auto = 0, Logprobs = 1, Verbalized = 2 }`
  - `public sealed class ChatDecisionClientOptions { ProbabilityMode ProbabilityMode { get; set; } = Auto; string? Model { get; set; } DecisionCapabilities? Capabilities { get; set; } }`
  - `public sealed class ChatDecisionClient : IDecisionClient`, with constructor `ChatDecisionClient(IChatClient chatClient, ChatDecisionClientOptions? options = null)`
  - `public static class ChatClientDecisionExtensions { public static ChatDecisionClient AsDecisionClient(this IChatClient chatClient, ChatDecisionClientOptions? options = null) }`
  - test helper `ScriptedChatClient : IChatClient`, which keeps the `(messages, ChatOptions)` of every call and answers from a `Func<IEnumerable<ChatMessage>, ChatOptions?, ChatResponse>` given in its constructor. Later tasks use it.

- [ ] **Step 1: Write the failing tests** in `ChatDecisionClientTests.cs`:
  - `NoQuestions_IsValidation_AndSendsNothing`: a request with an empty definition gives `DecisionErrorKind.Validation`, and the scripted client saw 0 calls.
  - `TooManyOptions_IsUnsupportedMIN203_AndSendsNothing`: a keyed Choice with 27 options gives `Unsupported`, and `Failures[0].Rule == "MIN203"`.
  - `Images_AreUnsupportedByDefault_MIN206`.
  - `GetService_ReturnsCapabilities`: `MaxChoiceOptions == 26`, every kind, `SupportsImages == false`, `SupportsModelListing == false`.
  - `GetService_ReturnsMetadata_FromTheChatClient`: inner `ChatClientMetadata("openrouter", new Uri("https://openrouter.ai/api/v1"), "m1")` maps to `DecisionClientMetadata("openrouter", that uri, "m1")`. With `options.Model = "m2"`, the default model is `"m2"`.
  - `GetService_ForwardsUnknownTypes_ToTheChatClient`.
  - `Dispose_LeavesTheChatClientUsable`: after `client.Dispose()`, the scripted client's disposed flag is false, and `EvaluateAsync` throws `ObjectDisposedException`.

- [ ] **Step 2: Run** `dotnet test tests/Minos.NET.Extensions.AI.Tests`. Expected: build fails.

- [ ] **Step 3: Implement** the skeleton. `EvaluateAsync` returns the errors above, and for now `Unsupported` for anything it would send.
  - The csproj references `Minos.NET`, `Minos.NET.DependencyInjection`, `Microsoft.Extensions.AI.Abstractions` and `OpenAI`. It sets `IsAotCompatible=true`, and `MinosNoValidationBaseline=true` as `Minos.NET.Recording.csproj:11` does, and it packs its own `README.md`.
  - The test project also references `Microsoft.Extensions.AI.OpenAI`.
  - `AotSmoke`'s `ExtensionsAIChecks` builds an OpenAI `ChatClient` with a dummy key and endpoint, wraps it with `.AsIChatClient().AsDecisionClient()`, and reads `GetService<DecisionCapabilities>()`. Each public entry point carries a `[Covers(...)]`.
  - `ExtensionsAIPackageTests` is modelled on `OpenAIPackageTests.cs:13`: `new PackedProject("Minos.NET.Extensions.AI", "Minos.NET", "Minos.NET.DependencyInjection")`.

- [ ] **Step 4: Run, one at a time:**
  - `dotnet test tests/Minos.NET.Extensions.AI.Tests`
  - `dotnet test tests/Minos.NET.AotSmoke.Tests`
  - `dotnet test tests/Minos.NET.PackTests`
  - `python3 -m pytest tools/release`

  Expected: all pass.

- [ ] **Step 5: Commit** (`feat: scaffold Minos.NET.Extensions.AI with capabilities and metadata`), then push the branch with `git push -u origin feat/extensions-ai-bridge`. Read the CI `aot-smoke` and `aot-surface` jobs on that push.
  - Expected: zero IL2 and IL3 warnings. Do not publish with AOT locally.
  - If the OpenAI or M.E.AI packages warn, stop and report to Marcel with the warning text.

### Task 5: Label distribution, the pure math

**Files:**
- Create: `src/Minos.NET.Extensions.AI/LabelDistribution.cs` (internal static)
- Test: `tests/Minos.NET.Extensions.AI.Tests/LabelDistributionTests.cs`

**Interfaces:**
- Produces:
  - `internal static string Label(int index)`, giving `"A"` and up
  - `internal static double[]? FromLogprobs(int labelCount, IEnumerable<(string Token, double Logprob)> top)`, which returns `null` when no label matches
  - `internal static double[]? FromStated(int labelCount, IReadOnlyList<double> stated)`, which returns `null` when malformed
  - `internal static (int Picked, double Confidence) Pick(double[] p)`
  - `internal static double Expected(double[] p)`

- [ ] **Step 1: Write the failing tests:**
  - `Logprobs_SoftmaxOverLabels`: tokens `A:ln 0.6`, `B:ln 0.3`, `C:ln 0.1` give `[0.6, 0.3, 0.1]` within 1e-9.
  - `Logprobs_TrimmedTokensAreSummed`: `"A":ln 0.4`, `" A":ln 0.2` and `"B":ln 0.4` give `[0.6, 0.4]`.
  - `Logprobs_LowercaseDoesNotCount`: `"a":ln 0.9` and `"B":ln 0.1`, with 2 labels, give `[0.9, 0.1]`. The `a` is ignored, so `A` is missing and gets the leftover 0.9.
  - `Logprobs_MissingLabelsShareTheLeftover`: `A:ln 0.5` and `B:ln 0.3`, with 4 labels, give `[0.5, 0.3, 0.1, 0.1]`.
  - `Logprobs_NoLabel_IsNull`.
  - `Stated_IsNormalized`: `[2, 1, 1]` gives `[0.5, 0.25, 0.25]`.
  - `Stated_Malformed_IsNull` (theory): NaN, a negative value, positive infinity, all zeros, and a wrong count.
  - `Pick_TieTakesTheLowerIndex`: `[0.4, 0.4, 0.2]` gives `(0, 0.4)`.
  - `Expected_IsTheWeightedLevel`: `[0.2, 0.5, 0.3]` gives `1.1`.

- [ ] **Step 2: Run** `dotnet test tests/Minos.NET.Extensions.AI.Tests --filter LabelDistributionTests`. Expected: build fails.

- [ ] **Step 3: Implement.** The logprob algorithm is:

```
p[i] = Σ exp(logprob) over tokens whose Trim() == Label(i)
if all p[i] == 0 → null
left = max(0, 1 − Σ p); missing = count of p[i] == 0
each missing p[i] = left / missing   (when missing > 0)
normalize p to sum 1
```

- [ ] **Step 4: Run** the same filter. Expected: PASS.

- [ ] **Step 5: Commit**: `feat: turn label logprobs or stated probabilities into a distribution`

### Task 6: Prompt and the logprob path

**Files:**
- Create:
  - `src/Minos.NET.Extensions.AI/QuestionPrompt.cs` (internal)
  - `src/Minos.NET.Extensions.AI/LogprobReader.cs` (internal)
- Modify: `ChatDecisionClient.cs`
- Test: `tests/Minos.NET.Extensions.AI.Tests/LogprobPathTests.cs` and `QuestionPromptTests.cs`

**Interfaces:**
- Consumes: `LabelDistribution` from Task 5 and `ScriptedChatClient` from Task 4.
- Produces:
  - `internal static IList<ChatMessage> QuestionPrompt.Build(QuestionDefinition question, DecisionRequest request, bool verbalized)`
  - `internal static IEnumerable<(string, double)>? LogprobReader.TopAtFirstToken(ChatResponse response)`. It returns `null` when `RawRepresentation` is not an `OpenAI.Chat.ChatCompletion` or has no content logprobs.
  - `ChatDecisionClient` answers in `ProbabilityMode.Logprobs` mode.
  - test helper `ChatCompletions.WithTopLogprobs(params (string Token, float Logprob)[] top)`, which builds a `ChatResponse` whose `RawRepresentation` is a `ChatCompletion` made with `OpenAIChatModelFactory`. Later tasks use it.

- [ ] **Step 1: Write the failing tests:**
  - `QuestionPromptTests`:
    - **The system message:** the prompt has exactly one system message, and it says to answer with one label.
    - **A Choice:** the user message lists `A. <option key>: <description>`, then each option's examples and `NotFor` lines, then the state text.
    - **A Noul:** the user message contains `A. Yes` and `B. No` and the `WhenTrue`/`WhenFalse` text.
    - **A Score:** the user message labels each level by index.
    - **With `verbalized: true`:** the message asks for the probability of each label.
  - `LogprobPathTests`:
    - **ChatOptions:** a logprob call sets `MaxOutputTokens == 1`, `Temperature == 0`, and `ModelId == request.Model ?? options.Model`. `RawRepresentationFactory` returns a `ChatCompletionOptions` with `IncludeLogProbabilities == true` and `TopLogProbabilityCount == 20`.
    - **A Noul:** top `A:ln 0.8`, `B:ln 0.2` gives `Probability == 0.8` and `ProbabilitySource == Logprobs`.
    - **A Choice:** `Confidence` and `ChosenIndex` come from `Pick`. A Score's `Expected` comes from `Expected`.
    - **No logprobs:** a response without logprobs in `Logprobs` mode gives `Unsupported`, and its message names the model and says it returned no logprobs.
    - **No label in the top tokens:** in `Logprobs` mode this gives `InvalidResponse`.
    - **The state stays out of errors:** the state text `"SECRET-STATE"` appears in no `DecisionError.Message`.

- [ ] **Step 2: Run** `dotnet test tests/Minos.NET.Extensions.AI.Tests --filter "LogprobPathTests|QuestionPromptTests"`. Expected: FAIL.

- [ ] **Step 3: Implement** the prompt and the logprob path for a single question. Images go in as `DataContent(image.Data, image.MediaType)` parts when `SupportsImages` is on. The templates are internal constants.

- [ ] **Step 4: Run** the same filter. Expected: PASS.

- [ ] **Step 5: Commit**: `feat: answer questions from the logprobs of an OpenAI-compatible chat model`

### Task 7: The verbalized path and `Auto`

**Files:**
- Create: `src/Minos.NET.Extensions.AI/VerbalizedReader.cs` (internal)
- Modify: `ChatDecisionClient.cs`
- Test: `tests/Minos.NET.Extensions.AI.Tests/VerbalizedPathTests.cs`

**Interfaces:**
- Consumes: `QuestionPrompt.Build(..., verbalized: true)` and `LabelDistribution.FromStated`.
- Produces:
  - `internal static ChatResponseFormat VerbalizedReader.Format(int labelCount)`. The schema is a hand-built `JsonElement`, not `AIJsonUtilities`, with schema name `"minos_answer"`. It is an object with one `number` property per label, `minimum` 0 and `maximum` 1, every label required, and `additionalProperties: false`.
  - `internal static double[]? VerbalizedReader.Read(string? text, int labelCount)`, which reads with `JsonDocument` and returns `null` when malformed.

- [ ] **Step 1: Write the failing tests:**
  - **The format:** in `Verbalized` mode the call has `ResponseFormat` set to that schema and `Temperature == 0`, with no `RawRepresentationFactory`. Assert the schema JSON text.
  - **A Choice:** the reply `{"A":0.7,"B":0.2,"C":0.1}` gives probabilities `[0.7, 0.2, 0.1]` and `ProbabilitySource == Verbalized`.
  - **A malformed reply** (theory) gives `InvalidResponse`. The cases are:
    - not JSON;
    - a missing label;
    - `"A":-0.1`;
    - all zeros;
    - an extra unknown label, which is ignored as long as every label is present.
  - **`Auto` falls back:** in `Auto` mode, the first call returns no logprobs and the second returns the JSON. The scripted client sees exactly 2 calls for that question, and the answer is `Verbalized`.
  - **`Auto` stays on logprobs:** in `Auto` mode, a first call with logprobs gives 1 call and `Logprobs`.
  - **`Auto` keeps no memory:** in `Auto` mode, two consecutive `EvaluateAsync` calls against a model without logprobs each try logprobs first, so the client sees 4 calls in all.
  - **No third attempt:** in `Auto`, a malformed verbalized reply gives `InvalidResponse`, after 2 calls.

- [ ] **Step 2: Run** `dotnet test tests/Minos.NET.Extensions.AI.Tests --filter VerbalizedPathTests`. Expected: FAIL.

- [ ] **Step 3: Implement.**

- [ ] **Step 4: Run** the same filter. Expected: PASS.

- [ ] **Step 5: Commit**: `feat: fall back to model-stated probabilities, marked as verbalized`

### Task 8: Question sets, parallel calls, usage and errors

**Files:**
- Create: `src/Minos.NET.Extensions.AI/ChatErrorMapper.cs` (internal)
- Modify: `ChatDecisionClient.cs`
- Test: `tests/Minos.NET.Extensions.AI.Tests/QuestionSetTests.cs` and `ErrorMappingTests.cs`

**Interfaces:**
- Consumes: `ErrorBodies.KindOf(int)` and the `DecisionError` init properties `StatusCode`, `RetryAfter`, `RequestId` and `Exception`.
- Produces: `internal static DecisionError? ChatErrorMapper.Map(Exception exception)`. It returns `null` for an exception that must propagate.

- [ ] **Step 1: Write the failing tests:**
  - `QuestionSetTests`:
    - **Matching by key:** a three-question set gives one call per question. The answers come back in definition order and are matched by question even when the scripted client completes them out of order (scripted delays).
    - **Usage:** `Usage.InputTokens` and `OutputTokens` are the sums over the calls. `Model` is the first response's `ModelId`.
    - **Typed evaluation works end to end:** `client.EvaluateAsync<T>(state)` over a generated record.
    - **The pipeline works:** with `new DecisionClientBuilder(client).UseRecording(recorder).Build()`, the record holds each answer's `ProbabilitySource`.
  - `ErrorMappingTests`:
    - **Status codes:** a `ClientResultException` with status 401 maps to `Unauthorized`, 400 and 422 to `Validation`, 429 to `RateLimited`, 503 to `Overloaded` and 500 to `Server`. On a 429, `RetryAfter` comes from `retry-after-ms` first, then from `Retry-After`, and `StatusCode` is set. Build the exception from a fake `PipelineResponse`.
    - **Transport failures:** `HttpRequestException` maps to `Network`, and `TimeoutException` to `Timeout`.
    - **Other exceptions propagate:** an `InvalidOperationException` from the chat client propagates out of `EvaluateAsync`.
    - **A failure cancels the rest:** one question failing with 429 while another hangs returns `RateLimited`, and the hanging call observes cancellation.
    - **Caller cancellation throws:** cancelling the caller's token throws `OperationCanceledException`, not a `DecisionError`.

- [ ] **Step 2: Run** `dotnet test tests/Minos.NET.Extensions.AI.Tests --filter "QuestionSetTests|ErrorMappingTests"`. Expected: FAIL.

- [ ] **Step 3: Implement.** Use a linked `CancellationTokenSource` and cancel it on the first failure. Results go into an array indexed by question.

- [ ] **Step 4: Run** `dotnet test tests/Minos.NET.Extensions.AI.Tests`. Expected: all pass.

- [ ] **Step 5: Commit**: `feat: ask a whole question set in parallel calls and map chat errors`

### Task 9: Dependency injection

**Files:**
- Create: `src/Minos.NET.Extensions.AI/ChatDecisionClientServiceCollectionExtensions.cs`
- Test: `tests/Minos.NET.Extensions.AI.Tests/DependencyInjectionTests.cs`

**Interfaces:**
- Consumes: the public `AddDecisionClient(factory)` and `AddDecisionClient(name, factory)`.
- Produces:
  - `public static DecisionClientServiceBuilder AddChatDecisionClient(this IServiceCollection services, Action<ChatDecisionClientOptions>? configure = null)`, which resolves `IChatClient`;
  - `public static DecisionClientServiceBuilder AddChatDecisionClient(this IServiceCollection services, string name, Action<ChatDecisionClientOptions>? configure = null)`, which resolves the keyed `IChatClient` registered under `name` and registers a keyed `IDecisionClient` `name`.

- [ ] **Step 1: Write the failing tests:**
  - **Default:** `services.AddChatClient(scripted); services.AddChatDecisionClient(o => o.ProbabilityMode = ProbabilityMode.Verbalized);` resolves an `IDecisionClient` whose `GetService<ChatDecisionClient>()` is not null.
  - **Recording chains:** `.UseRecording()` chains, and a decision is recorded through the container's recorder.
  - **Keyed:** `AddKeyedChatClient("triage", scripted)` plus `AddChatDecisionClient("triage")` resolves the keyed decision client over that chat client.
  - **Missing chat client:** resolving without any `IChatClient` registered throws `InvalidOperationException` naming `IChatClient`.

- [ ] **Step 2: Run** `dotnet test tests/Minos.NET.Extensions.AI.Tests --filter DependencyInjectionTests`. Expected: FAIL.

- [ ] **Step 3: Implement**, and add the PublicAPI entries and their AotSmoke `[Covers]` checks.

- [ ] **Step 4: Run** `dotnet test tests/Minos.NET.Extensions.AI.Tests`, then `dotnet test tests/Minos.NET.AotSmoke.Tests`. Expected: PASS.

- [ ] **Step 5: Commit**: `feat: register a chat-backed decision client with AddChatDecisionClient`

### Task 10: The chat-completions conformance wire

**Files:**
- Modify:
  - `src/Minos.NET.Conformance/ConformanceTarget.cs` (enum)
  - `ConformanceFixture.cs`: the doc, plus a new method `FindAll(probe)`
  - `FixtureHandler.cs`, `ConformanceRunner.cs` (lines 87-91 and `CheckCovers` at 159), `ConformanceChecks.cs` and `WireAnswers.cs`
- Test: `tests/Minos.NET.Conformance.Tests/ChatWireTests.cs`

**Interfaces:**
- Produces:
  - `ConformanceWire.ChatCompletions`.
  - **Fixtures:** on this wire only, a probe may hold several exchanges.
  - **Replay:** `FixtureHandler` answers a request with the first unanswered exchange of that probe whose recorded `request` JSON equals the incoming body, compared as parsed JSON. A request matching none throws `InvalidOperationException` naming the probe.
  - **Record:** on this wire the runner saves every exchange, not `exchanges[^1]`.
  - **`CheckCovers`:** allows repeated probe ids only on this wire.
  - **Checks on this wire:**
    - S1 and S2 skip with `"A chat completion carries no answer body; the answers are checked as Minos read them."`
    - D1 to D4 and M1 run on `DecisionResponse` and its answers: probabilities finite and in [0, 1], sums within 0.01, `Expected` against the levels, confidence in [0, 1], and `mixed` matched by key.
    - R1 and R3 read the root `model` and `usage.prompt_tokens`/`completion_tokens` of each exchange body.
    - E1 expects `Validation`, with a message and no request sent.
    - E2 is unchanged: `bad-key` is one question, so one exchange.

- [ ] **Step 1: Write the failing tests** in `ChatWireTests.cs`, with a tiny in-memory fixture built in the test:
  - **Replay matches by body:** replay serves two exchanges of `mixed` that arrive in reverse order.
  - **No match throws:** an unmatched body throws.
  - **Record keeps every exchange:** with a scripted network handler, Record saves 3 exchanges for `mixed`.
  - **S1 and S2 skip** with the exact reason.
  - **D2 fails on bad sums:** it fails for a response whose Choice probabilities sum to 0.9.
  - **E1 passes when nothing is sent:** it passes on a `Validation` error with nothing sent.
  - **Other wires are unchanged:** repeated probe ids on the `SystemOne` wire still fail `CheckCovers`.

- [ ] **Step 2: Run** `dotnet test tests/Minos.NET.Conformance.Tests --filter ChatWireTests`. Expected: FAIL.

- [ ] **Step 3: Implement.** The `SystemOne` and `Decisions` paths must be untouched.

- [ ] **Step 4: Run** `dotnet test tests/Minos.NET.Conformance.Tests`. Expected: all pass, and `ReportTests`' snapshot is unchanged.

- [ ] **Step 5: Commit**: `feat: conformance can check a chat-completions endpoint`

### Task 11: The chat fixture, replay in CI, and the live recording test

**Files:**
- Create:
  - `tests/Minos.NET.Conformance.Tests/Fixtures/chat-openrouter.json`
  - `tests/Minos.NET.Conformance.Tests/ChatTargets.cs`
  - `tests/Minos.NET.Conformance.Tests/ScriptedChatNetwork.cs`
- Modify:
  - `tests/Minos.NET.Conformance.Tests/Minos.NET.Conformance.Tests.csproj`: references to the bridge and `Microsoft.Extensions.AI.OpenAI`
  - `Fixtures.cs`: `Replay` switch
  - `ReplaySuiteTests.cs`: `ExpectedSkips` and `Targets`
  - `FixtureFileTests.cs:104-107`: the file list
  - `tests/Minos.NET.Live.Tests/ConformanceRecordTests.cs`
  - `.github/workflows/live-smoke.yml`, only if a new env var is needed

**Interfaces:**
- Consumes: the bridge from Tasks 4-9 and the wire from Task 10.
- Produces:
  - `ChatTargets.OpenRouter(string model, int maxRetries)` gives a `ConformanceTarget("chat-openrouter", ConformanceWire.ChatCompletions, model, rule, http => …)`.
  - The factory builds an OpenAI SDK `ChatClient(model, new ApiKeyCredential(key), new OpenAIClientOptions { Endpoint = new Uri("https://openrouter.ai/api/v1"), Transport = new HttpClientPipelineTransport(http), RetryPolicy = new ClientRetryPolicy(0) })`, then `.AsIChatClient().AsDecisionClient(new() { ProbabilityMode = ProbabilityMode.Logprobs, Model = model })`.
  - The key is `OPENROUTER_API_KEY`, or `"replay-key"` in Replay.
  - The model rule: the reported model starts with the requested one after an optional provider prefix. Write the predicate and its description string.

- [ ] **Step 1: Write the failing tests:**
  - **The fixture is current:** `ChatFixture_IsWhatTheScriptedNetworkRecords`. It runs Record against `ScriptedChatNetwork`, a `HttpMessageHandler` that returns canned chat completions with top logprobs per prompt, then `bad-key` with a 401. It then sets `source: "tests"` and `note: "Built from the bridge's scripted responses; the live recording follows from live-smoke."`. The result must equal the committed `chat-openrouter.json`. This keeps the fixture in step with prompt changes.
  - **`ReplaySuiteTests` covers `chat-openrouter`:**
    - `ExpectedSkips` holds exactly `S1` and `S2` for each answer probe, the checks those probes skip by kind as the other targets do, plus `run:E4` and `invalid:E3`, as the current targets list.
    - Fill in the exact set from the first green run and review it.
  - **`FixtureFileTests`** lists `chat-openrouter.json`.
  - **Live test:** `ConformanceRecordTests` gains a `[LiveFact(SystemOneProvider.OpenRouter)]` test that records `chat-openrouter.json`. Its model comes from `MINOS_LIVE_CHAT_MODEL`, with default `openai/gpt-4.1-mini`.

- [ ] **Step 2: Run** `dotnet test tests/Minos.NET.Conformance.Tests`. Expected: FAIL, because the fixture is missing.

- [ ] **Step 3: Generate the fixture** with the helper's write mode: an env var such as `MINOS_WRITE_CHAT_FIXTURE=1` writes to `Fixtures.SourceDirectory`. Commit it.

- [ ] **Step 4: Run** `dotnet test tests/Minos.NET.Conformance.Tests`. Expected: all pass, and `Replay_FailsNoCheck` is green for `chat-openrouter`.

- [ ] **Step 5: Commit**: `test: replay the chat bridge through the conformance suite`

### Task 12: Sample

**Files:**
- Create: `samples/Minos.NET.Samples.ChatClient/`, with:
  - the csproj (Exe, `IsPackable=false`)
  - `Program.cs`, `ChatClientApp.cs`, `TriageQuestions.cs`, `FakeChatClient.cs`, `README.md`
- Modify: `Minos.NET.slnx` and `samples/README.md` (the sample table)
- Test: `tests/Minos.NET.Samples.Tests/ChatClientSampleTests.cs`, plus its snapshot

**Interfaces:**
- Consumes: `AddChatDecisionClient`, `UseRecording` and `AsIChatClient`.
- Produces: `ChatClientApp.RunAsync(string[] args, string baseDirectory, TextWriter output, TextWriter error, CancellationToken ct) -> Task<int>`.
  - It runs a `[Questions]` record with a Noul and a Choice over 3 example messages.
  - It prints each answer, its probabilities and its `ProbabilitySource`.
  - With `OPENROUTER_API_KEY` set, it uses OpenRouter through the OpenAI client with model `openai/gpt-4.1-mini`, or `--model`.
  - Without a key, it uses `FakeChatClient`. That client gives logprobs for the first message and plain JSON for the rest, so both marks show.

- [ ] **Step 1: Write the failing test** `RunsOffline_AndShowsBothSources`. Run the app with the key empty, and snapshot the output with `TextSnapshot.VerifyText`. The output contains `Logprobs` and `Verbalized`.

- [ ] **Step 2: Run** `dotnet test tests/Minos.NET.Samples.Tests --filter ChatClientSampleTests`. Expected: FAIL.

- [ ] **Step 3: Implement the sample.**

- [ ] **Step 4: Run** the same filter, then review and accept the snapshot. Expected: PASS.

- [ ] **Step 5: Commit**: `docs: add a chat-client sample`

### Task 13: Docs page, README and page tests

**Files:**
- Create:
  - `docs/chat-clients.md`, with front matter `id: chat-clients` and the next free `sidebar_position` after `providers`, shifting later pages if `ReadmeLinkTests` needs it
  - `tests/Minos.NET.Docs.Tests/ChatClients.cs`, with the snippet regions
- Modify:
  - `docs/providers.md`: the at-a-glance table gets a "Chat client" column, plus a short section linking the new page
  - `docs/choosing-a-provider.md`
  - `docs/conformance.md`: the chat wire, its fixture source, multi-exchange replay, and the S1/S2/E1 rules
  - `README.md`: the package table, badges and Documentation list
  - `tests/Minos.NET.Docs.Tests/Minos.NET.Docs.Tests.csproj`: reference the new package
  - `VersionLineTests.cs:13`
  - any page test that pins the providers or conformance tables, such as `ProvidersPageTests` and `ConformancePageTests`

**Interfaces:**
- Consumes: everything public from Tasks 1-9.

- [ ] **Step 1: Write the snippets first.** Put them in `ChatClients.cs` as compiling regions:
  - `ChatClients_AsDecisionClient`
  - `ChatClients_Register`, for DI with `UseRecording`
  - `ChatClients_ReadSource`, which reads `ProbabilitySource` from a typed answer

- [ ] **Step 2: Write the page.** It covers:
  - what the bridge is;
  - the three `ProbabilityMode`s;
  - what `Logprobs` and `Verbalized` mean;
  - that bridge confidence is the picked option's probability and is not TypeSafe's calibrated confidence;
  - that reasoning models get `Verbalized`, so set `Verbalized` to skip the wasted logprob call;
  - that the bridge does not dispose the chat client;
  - capabilities;
  - errors.

  Then run `dotnet tool restore && dotnet mdsnippets`. Expected: snippets are filled in, and `git diff` shows only the intended pages.

- [ ] **Step 3: Run** `dotnet test tests/Minos.NET.Docs.Tests`. Expected: PASS, after updating the page tests that pin the changed tables.

- [ ] **Step 4: Run the whole solution once**: `dotnet build Minos.NET.slnx -c Release`, then `dotnet test Minos.NET.slnx -c Release --no-build`. Expected: zero warnings and all tests pass.

- [ ] **Step 5: Commit**: `docs: document chat clients as a decision provider`

### After the tasks

- [ ] Run the pre-push review, then push and open the PR `feat: add Minos.NET.Extensions.AI, an IDecisionClient over IChatClient`, filling in the template if one exists. The body has no Claude signature or session link. Marcel merges.
- [ ] After the merge:
  - confirm the commit is listed in the release-please PR;
  - dispatch live-smoke on `main` and bring the recorded `chat-openrouter.json` in through a follow-up PR;
  - once released, check `Minos.NET.Extensions.AI` and the bumped core on nuget.org.
