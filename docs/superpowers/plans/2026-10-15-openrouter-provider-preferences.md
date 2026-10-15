# OpenRouter Provider Preferences Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A `SystemOneDecisionClient` configured for OpenRouter can require zero data retention and no data collection. Every request it sends then carries OpenRouter's `provider` object (#202).

**Architecture:**
- Options gain `ProviderPreferences`. `SystemOneDecisionClientSettings.Resolve` validates it and turns it into fixed UTF-8 bytes once per client.
- The neutral and typed path writes those bytes through a per-client `SystemOneProtocol`. The raw `EvaluateAsync(SystemOneRequest)` path writes them through an internal member of `SystemOneRequest`.
- A live test proves `/v1/systemone` reads `provider`. It runs on `main` after the merge.

**Tech Stack:** .NET 10, C#, System.Text.Json source generation, xUnit, ZeroAlloc.Rest, Native AOT smoke app.

**Spec:** `docs/superpowers/specs/2026-10-15-openrouter-provider-preferences-design.md`

## Global Constraints

- **Public API, additive only:** `OpenRouterProviderPreferences`, sealed, with settable `bool ZeroDataRetention` and `bool DenyDataCollection`, plus `SystemOneDecisionClientOptions.ProviderPreferences`, typed `OpenRouterProviderPreferences?`. Both go in `src/Minos.NET.SystemOne/PublicAPI.Unshipped.txt`. Nothing else public changes, and `SystemOneRequest`'s public shape stays the same.
- **Wire bytes, exactly**, appended after `questions`:
  - `,"provider":{"zdr":true}`
  - `,"provider":{"data_collection":"deny"}`
  - `,"provider":{"zdr":true,"data_collection":"deny"}`
  - nothing at all when the preferences are null or both flags are false.
- **Validation:** preferences with any flag true on a provider other than `SystemOneProvider.OpenRouter` make `Resolve` throw `ArgumentException`, with this message:

  > ProviderPreferences are OpenRouter's provider routing settings; Provider is {provider}, which does not support them.

  `Validate()` and the options validator surface it, as they do the other option errors.
- **Allocation budgets in `samples/Minos.NET.AotSmoke/AllocationChecks.cs` do not change**, including the raw `EvaluateAsync` budget of 4352 B. Run the AOT smoke app for any change under `src/`.
- **Errors:** no new `DecisionErrorKind`. A refusal keeps the generic `Message`, puts OpenRouter's body in `Detail`, and is not retried.
- **Commits:**
  - conventional, with a scope from `.commitlintrc.yml`: `systemone`, `docs` or `tests`;
  - no nested parentheses in bodies;
  - end with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

## Review Focus

1. **The raw path with a caller-supplied `Model`:** the client sends the caller's request object as is today, so the `provider` bytes must still be added. Covered in Task 3.
2. **Preferences with both flags false on TypeSafe:** they must not throw, and must send nothing. Covered in Task 1.
3. **A body with images:** the `provider` member still lands after `questions`, and the body stays valid JSON. Covered in Task 2.
4. **Options bound from `IConfiguration`:** `Minos:ProviderPreferences:ZeroDataRetention: true` binds through the source-generated binder, which only sees public settable members. Covered in Task 4.
5. **An OpenRouter refusal is not retried, even with `MaxRetries` 2.** Covered in Task 3.

---

### Task 1: The options type, validation and resolved bytes

**Files:**
- Create: `src/Minos.NET.SystemOne/OpenRouterProviderPreferences.cs`
- Modify: `src/Minos.NET.SystemOne/SystemOneDecisionClientOptions.cs`: the property, its XML docs, and the `Validate()` `<exception>` docs
- Modify: `src/Minos.NET.SystemOne/Transport/SystemOneDecisionClientSettings.cs`: `Resolve` validates and computes the bytes
- Modify: `src/Minos.NET.SystemOne/PublicAPI.Unshipped.txt`
- Test: `tests/Minos.NET.Tests/SystemOneDecisionClientSettingsTests.cs`, `tests/Minos.NET.Tests/DecisionClientOptionsValidateTests.cs`

**Interfaces:**
- Produces:
  - `public sealed class OpenRouterProviderPreferences { public bool ZeroDataRetention { get; set; } public bool DenyDataCollection { get; set; } }`
  - `public OpenRouterProviderPreferences? ProviderPreferences { get; set; }` on `SystemOneDecisionClientOptions`
  - `internal ReadOnlyMemory<byte> ProviderMember { get; }` on `SystemOneDecisionClientSettings`. It holds the exact bytes from Global Constraints, or empty.

- [ ] **Step 1: Write the failing tests** in `SystemOneDecisionClientSettingsTests`:
  - `ProviderMember_IsEmpty_WithoutPreferences`: no preferences, and `new()` with both flags false. `ProviderMember.IsEmpty` is true in both cases.
  - `ProviderMember_WritesEachFlag`: a theory over `(zdr, deny, expected)`:
    - `(true, false, ",\"provider\":{\"zdr\":true}")`
    - `(false, true, ",\"provider\":{\"data_collection\":\"deny\"}")`
    - `(true, true, ",\"provider\":{\"zdr\":true,\"data_collection\":\"deny\"}")`

    Provider OpenRouter with `OPENROUTER_API_KEY` set. Assert `Encoding.UTF8.GetString(settings.ProviderMember.Span) == expected`.
  - `ProviderPreferences_OnAnotherProvider_Throws`: a theory over `TypeSafe`, `VllmSemanticRouter` and `Custom`, the last with a base address. With `ZeroDataRetention = true`, `Assert.Throws<ArgumentException>` with the exact message from Global Constraints.
  - `AllFalsePreferences_OnTypeSafe_Resolve`: no throw.

  In `DecisionClientOptionsValidateTests`, add a case where `ZeroDataRetention` on TypeSafe makes `options.Validate()` throw the same message.

- [ ] **Step 2: Run them and see them fail**

  Run: `dotnet test tests/Minos.NET.Tests -c Release --filter "FullyQualifiedName~SystemOneDecisionClientSettingsTests|FullyQualifiedName~DecisionClientOptionsValidateTests"`
  Expected: build errors, because `ProviderPreferences` is not defined.

- [ ] **Step 3: Implement.**
  - Add the type and the property, with XML docs that say:
    - OpenRouter only; the client refuses to start on any other provider;
    - the account-wide setting still applies;
    - a refusal is a `DecisionError` that is not retried.
  - In `Resolve`, after `EnsureKnownProvider`, check the rule, then build the bytes once. A static `byte[]` per flag combination is fine.
  - Add both public members to `PublicAPI.Unshipped.txt`, in the format the existing entries use.

- [ ] **Step 4: Run the tests and see them pass**

  Run: the same command. Expected: all pass, with RS0016 and RS0017 clean in the build.

- [ ] **Step 5: Commit**

  ```bash
  git add src/Minos.NET.SystemOne tests/Minos.NET.Tests
  git commit -m "feat(systemone): OpenRouter provider preferences on the client options" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
  ```

---

### Task 2: The neutral and typed request body

**Files:**
- Modify: `src/Minos.NET.SystemOne/Protocols/SystemOneRequestWriter.cs`: `Write` takes `ReadOnlySpan<byte> providerMember`, written raw after `questions`, and the size hint grows by its length
- Modify: `src/Minos.NET.SystemOne/Protocols/SystemOneProtocol.cs`:
  - an internal constructor taking `ReadOnlyMemory<byte> providerMember`;
  - `Instance` keeps the empty one;
  - `WriteRequest` passes the bytes;
  - the questions cache stays on the definition, as it is now.
- Modify: `src/Minos.NET.SystemOne/SystemOneDecisionClient.cs`, near line 239: use `SystemOneProtocol.Instance` when `settings.ProviderMember` is empty, else `new SystemOneProtocol(settings.ProviderMember)`. Use that protocol for both `DecisionTransport` and `DecisionErrorMapper`.
- Test: `tests/Minos.NET.Tests/DecisionTransportTests.cs`, or a new `tests/Minos.NET.Tests/ProviderPreferencesBodyTests.cs` built on `ClientTestKit.Client(..., provider: SystemOneProvider.OpenRouter, configure: o => o.ProviderPreferences = …)`

**Interfaces:**
- Consumes: `SystemOneDecisionClientSettings.ProviderMember`, from Task 1.
- Produces: `SystemOneRequestWriter.Write(…, string model, ReadOnlySpan<byte> providerMember, ArrayPool<byte> pool)`. Every existing caller passes `default`.

- [ ] **Step 1: Write the failing tests** in `ProviderPreferencesBodyTests`, using the `StubHandler` capture:
  - `TypedCall_WithBothFlags_SendsProviderObject`: parse the captured `Body` with `JsonNode`.
    - `body["provider"]` deep-equals `{"zdr":true,"data_collection":"deny"}`.
    - With `provider` removed, the body deep-equals `ClientTestKit.ExpectedBody(request)`.
    - The raw text ends with `,"provider":{"zdr":true,"data_collection":"deny"}}`.
  - `TypedCall_WithoutPreferences_BodyUnchanged`: the captured body text equals today's bytes from `ExpectedBody`, compared as strings after serializing the `JsonNode`.
  - `CallWithImages_StillEndsWithProvider`: one `DecisionImage`, `ZeroDataRetention` only. The body parses, `state` is the parts array, and `provider` is `{"zdr":true}`.

- [ ] **Step 2: Run them and see them fail**

  Run: `dotnet test tests/Minos.NET.Tests -c Release --filter FullyQualifiedName~ProviderPreferencesBodyTests`
  Expected: FAIL, because `provider` is missing from the body.

- [ ] **Step 3: Implement** the writer parameter, the protocol constructor and the client wiring above.

- [ ] **Step 4: Run them and the whole `Minos.NET.Tests` and `Minos.NET.Conformance.Tests` projects**

  Expected: all pass. The conformance fixtures prove the body is unchanged without preferences.

- [ ] **Step 5: Commit**

  `feat(systemone): send OpenRouter provider preferences on typed and neutral calls`

---

### Task 3: The raw request body, and refusals are not retried

**Files:**
- Modify: `src/Minos.NET.SystemOne/SystemOneRequest.cs`:
  - an internal init-only member that carries the client's provider bytes;
  - it serializes as `"provider"` after `questions`, through a small internal `JsonConverter` that writes the cached bytes with `WriteRawValue`;
  - it is ignored when empty or null;
  - it is not public, and not bound or read on deserialization.

  Keep `[JsonPropertyOrder]` such that `provider` comes last.
- Modify: `src/Minos.NET.SystemOne/Serialization/DecisionJsonContext.cs`, only if the source generator needs the converter registered there.
- Modify: `src/Minos.NET.SystemOne/SystemOneDecisionClient.cs`, `EvaluateAsync(SystemOneRequest)` near line 397. When `ProviderMember` is non-empty, the copy that is sent carries it, and the caller's request object is never changed.

  Today the copy happens only when `Model` is null. With preferences it also happens when the caller names a model. When preferences are off, the path is exactly today's, so the 4352 B budget holds.
- Test: `tests/Minos.NET.Tests/ProviderPreferencesBodyTests.cs`, and the retry case beside the existing retry tests

**Interfaces:**
- Consumes: `SystemOneDecisionClientSettings.ProviderMember`, from Task 1.

- [ ] **Step 1: Write the failing tests:**
  - `RawCall_WithPreferences_SendsProviderObject`: once with `Model = null` and once with `Model = "jev-x"`.
    - The captured body's `provider` equals `{"zdr":true}`.
    - `model` is the expected one.
    - `state` and `questions` are unchanged.
    - The caller's `SystemOneRequest` instance is still the same object, with the same `Model`.
  - `RawCall_WithoutPreferences_BodyUnchanged`: there is no `provider` key.
  - `SystemOneRequest_Serialized_ByCaller_HasNoProvider`: `JsonSerializer.Serialize(request, DecisionJsonContext.Default.SystemOneRequest)` on a public-constructed request has no `provider` key.
  - `OpenRouterRefusal_IsNotRetried`: the handler answers 404 with the body `{"error":{"message":"No endpoints found matching your data policy","code":404}}`. The client uses `MaxRetries = 2` and `ZeroDataRetention`.
    - The result is a failure with `StatusCode` 404.
    - `Message` is "The API returned HTTP 404.".
    - `Detail` contains the body.
    - The handler saw exactly one request.

- [ ] **Step 2: Run them and see them fail.** The retry test may already pass; keep it, because it pins the behaviour.

- [ ] **Step 3: Implement** the internal member, the converter and the client change.

- [ ] **Step 4: Run all tests, then the AOT smoke app**

  Run: `dotnet test Minos.NET.slnx -c Release`
  Then publish and run `samples/Minos.NET.AotSmoke` as `ci.yml`'s `aot-smoke` job does:

  ```bash
  dotnet publish samples/Minos.NET.AotSmoke/Minos.NET.AotSmoke.csproj -r linux-x64 -c Release -o ./aot-out
  ./aot-out/Minos.NET.AotSmoke
  ```

  Expected: every allocation check within its unchanged budget, and no IL2xxx or IL3xxx warnings.

- [ ] **Step 5: Commit**

  `feat(systemone): send OpenRouter provider preferences on raw calls`

---

### Task 4: Configuration binding, AOT surface and docs

**Files:**
- Test: `tests/Minos.NET.DependencyInjection.Tests/AddSystemOneDecisionClientConfigurationTests.cs`
- Modify: `samples/Minos.NET.AotSurface`, if it roots every public member explicitly; root `OpenRouterProviderPreferences` there
- Modify: `docs/providers.md`. Add a `### Privacy: zero data retention` subsection under the OpenRouter content, near "Provider differences", with:
  - an options snippet, through the existing mdsnippets mechanism;
  - an `appsettings.json` example, keys `Minos:Provider` and `Minos:ProviderPreferences:ZeroDataRetention`;
  - the refusal on other providers;
  - that the account setting still applies;
  - that a refusal is an `Http` `DecisionError` that is not retried;
  - a placeholder line, "Verified live: pending", which Task 5 replaces.

  The existing "Notes from the probes" `zdr-violation-by-account` line must stay consistent with this subsection.
- Test: `Minos.NET.Docs.Tests` runs as is

- [ ] **Step 1: Write the failing binding test** `Binds_ProviderPreferences`:
  - in-memory config `Minos:Provider=OpenRouter`, `Minos:ProviderPreferences:ZeroDataRetention=true` and `Minos:ProviderPreferences:DenyDataCollection=true`;
  - resolve the options the way the neighbouring tests do;
  - assert both flags are true.

- [ ] **Step 2: Run it.** It passes if the source-generated binder already picks up the new property. A pass is acceptable, because the test pins it.

- [ ] **Step 3: Write the docs subsection and the snippet;** run `dotnet mdsnippets` as the docs check in `ci.yml` does.

- [ ] **Step 4: Run** `dotnet test tests/Minos.NET.Docs.Tests tests/Minos.NET.DependencyInjection.Tests -c Release`. Expected: pass.

- [ ] **Step 5: Commit**

  `docs(docs): OpenRouter zero data retention on the Providers page`

---

### Task 5: Live tests, the PR, and the post-merge verification

**Files:**
- Modify: `tests/Minos.NET.Live.Tests/OpenRouterLiveTests.cs`
- Modify: `docs/providers.md`: replace "Verified live: pending" after the run, in the follow-up PR
- Create: `docs/plans/2026-10-15-openrouter-provider-preferences-pre-push-review.md`

**Interfaces:**
- Consumes: `OpenRouterProviderPreferences`, from Task 1. `Live.cs` has `LogError`, `AssertAnsweredEveryQuestion` and `Request(string model)`.

- [ ] **Step 1: Add three `[LiveFact(SystemOneProvider.OpenRouter)]` tests.** All three pin `SystemOneDefaults.Model`, ignoring `MINOS_LIVE_OPENROUTER_MODEL`.
  - **`Endpoint_ReadsTheProviderObject`.** Post a body straight to `https://openrouter.ai/api/v1/systemone`:
    - `Live.Request(...)` serialized, plus `"provider":{"only":["no-such-provider"]}`;
    - with an `HttpClient` and `Authorization: Bearer $OPENROUTER_API_KEY`.

    Assert a non-success status, and write the status and body to the output.
  - **`ZeroDataRetention_Answers`.** A client with `ZeroDataRetention = true` and `Live.Request(SystemOneDefaults.Model)`. Assert success and `AssertAnsweredEveryQuestion`.
  - **`DenyDataCollection_AnswersOrRefuses`.** A client with `DenyDataCollection = true`.
    - Assert that either it succeeds, or it fails with `StatusCode` 404 or 400 and no retry.
    - Write which outcome, and the `Detail`, to the output.

- [ ] **Step 2: Build the live project.** Run it locally without keys; the three tests skip.

  Run: `dotnet test tests/Minos.NET.Live.Tests -c Release`
  Expected: skipped, 0 failed.

- [ ] **Step 3: Pre-push review, the PR, and the merge.**
  - Run `pre-push-review`; the report goes in `docs/plans/`.
  - Open the PR titled `feat(systemone): OpenRouter provider preferences, zero data retention`, with `Closes #202` left out until the live run passes.
  - The maintainer merges.

- [ ] **Step 4: The live run on `main`.**

  Run: `gh workflow run live-smoke.yml --ref main`. This is approved by the maintainer; it makes a few billed calls.

  Expected:
  - `Endpoint_ReadsTheProviderObject` passes;
  - `ZeroDataRetention_Answers` passes;
  - `DenyDataCollection_AnswersOrRefuses` passes and records its outcome.

  **If `Endpoint_ReadsTheProviderObject` fails**, OpenRouter got an answer, so the endpoint ignores `provider`. Stop:
  - do not merge the release PR;
  - revert the feature, or mark it unenforced;
  - report to the maintainer.

- [ ] **Step 5: The follow-up PR.**
  - `docs/providers.md` gets the run link, what was proven, and the DenyDataCollection outcome.
  - Close #202 with the run link.
  - The release PR proposing 1.1.0 can then merge.
