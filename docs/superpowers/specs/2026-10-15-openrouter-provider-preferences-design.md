# OpenRouter provider preferences — design

**Date:** 2026-10-15
**Issue:** #202
**Surface:** Backend
**Release:** 1.1.0, additive public API in `Minos.NET.SystemOne`. No milestone; a 1.x feature between Milestones 7 and 8.

## Why

Daedalus sends task text to Minos through OpenRouter. Today its only privacy safeguard is the account-wide setting at openrouter.ai/settings/privacy. Daedalus turns decisions on only when its owner sets `Thalos:Decisions:ZeroDataRetentionAccount: true`, which means trusting that the account really is restricted.

A request cannot enforce the guarantee itself, so one misconfigured account or key leaks task text to a provider that retains it. With this change, a client *requires* zero data retention, and OpenRouter refuses rather than routes elsewhere.

## Facts this design rests on

- **OpenRouter's `provider` request object** has `zdr` (boolean) and `data_collection` (`"allow"` or `"deny"`), among others (openrouter.ai/docs/features/provider-routing):
  - `zdr: true` restricts routing to Zero Data Retention endpoints.
  - `data_collection: "deny"` restricts it to providers that don't collect user data.
  - `zdr: false` or a missing field has no effect.
- **OpenRouter documents `provider` for Chat Completions only.** Whether its `/v1/systemone` endpoint honours it is **unverified**. The live check below settles it.
- **Jev on OpenRouter is ZDR.** OpenRouter's public ZDR endpoint list (`GET https://openrouter.ai/api/v1/endpoints/zdr`, 2026-10-15) includes `typesafe/jev-1.13` and `typesafe/jev-1.14`, both served by TypeSafe, the only provider. So `zdr: true` still routes.
- **Daedalus registers one client at startup:** `AddSystemOneDecisionClient(o => { o.Provider = SystemOneProvider.OpenRouter; … })`. It does not vary privacy per call.
- **Code today:**
  - `SystemOneRequestWriter.Write` writes `{"state":…,"model":…,"questions":…}`.
  - `SystemOneProtocol` is a shared singleton, `SystemOneProtocol.Instance`, whose cached `questions` bytes are per definition.
  - `SystemOneDecisionClient` resolves its options into `SystemOneDecisionClientSettings`.
  - `SystemOneDecisionClientOptionsValidator` validates options at startup.
  - `ErrorBodies.KindOf` maps 404 to `DecisionErrorKind.Http` and 400 to `Validation`. Neither is retried.
  - The raw `EvaluateAsync(SystemOneRequest)` call does not use `SystemOneRequestWriter`. It serializes the public `SystemOneRequest` with System.Text.Json through `IDecisionApi` and `DecisionJsonContext` (`SystemOneDecisionClient.cs` around line 406).
- **The refusal is already on record.** `docs/providers.md` ("Notes from the probes", 2026-10-11) records OpenRouter refusing on ZDR grounds:
  - HTTP 404 with `zdr-violation-by-account`, an account-level refusal;
  - Minos reports it as `Http` and does not retry.
- **The maintainer's OpenRouter account is already ZDR-restricted**, per the same section. So a live `zdr: true` call succeeds whether or not the endpoint reads the flag.

## Decisions, by the maintainer on 2026-10-15

| Question | Decision |
|---|---|
| Where the preferences are set | **On the client options only:** `SystemOneDecisionClientOptions`. Not on `DecisionRequest`; the core stays provider-neutral. Per-call preferences are a later issue if someone needs them. |
| Set with a provider other than OpenRouter | **Refuse at startup.** Options validation fails with a message saying these preferences exist only on OpenRouter. A privacy guarantee must never silently do nothing. |
| Live verification | **Approved:** a few billed OpenRouter calls through live-smoke. |
| What proves the feature works | **That `/v1/systemone` reads the `provider` object:** a provider object that cannot be satisfied is refused. `zdr` and `data_collection` are documented fields of that object. No second, unrestricted account is added to prove `zdr` alone, and the docs state this limit. |
| `data_collection: "deny"` refuses Jev | **Keep the option and document it.** If the live check shows `DenyDataCollection` alone refuses Jev, the docs say it currently blocks Jev on OpenRouter; refusing beats leaking. The `zdr: true` call is the one that must succeed. |

## Design

**Public API**, new in `Minos.SystemOne`, recorded in `PublicAPI.Unshipped.txt`:

```csharp
/// OpenRouter's provider routing preferences, sent with every request as OpenRouter's "provider" object.
public sealed class OpenRouterProviderPreferences
{
    /// Requires Zero Data Retention endpoints: "zdr": true.
    public bool ZeroDataRetention { get; set; }

    /// Refuses providers that collect user data: "data_collection": "deny".
    public bool DenyDataCollection { get; set; }
}

// On SystemOneDecisionClientOptions:
public OpenRouterProviderPreferences? ProviderPreferences { get; set; }
```

The class is mutable with setters, like the options class, so `IConfiguration` binding works. For example, `Minos:ProviderPreferences:ZeroDataRetention: true` binds through the existing `Bind`; the configuration binder source generator already runs for these options. Only these two OpenRouter fields are in scope.

**Validation**, in `SystemOneDecisionClientOptionsValidator` and `Validate()`. When `ProviderPreferences` is non-null with at least one flag true, and `Provider` is not `OpenRouter`, validation fails. The message names the provider, for example: "ProviderPreferences are OpenRouter's provider routing settings; Provider is TypeSafe, which does not support them." Preferences with both flags false are allowed on any provider and send nothing.

**Writing, on both request paths.**
- When the client is built, the resolved settings turn the preferences into fixed UTF-8 bytes for the `provider` member:
  - `,"provider":{"zdr":true,"data_collection":"deny"}` with both flags;
  - only the members whose flag is true otherwise;
  - empty when neither flag is set.
- `SystemOneRequestWriter.Write` writes those bytes after `questions`, before the closing brace.
- The client must hand the bytes to the writer without making the shared protocol singleton per-client state. Whether that is a protocol instance per client or an extra parameter is the builder's call.
- **The raw path must send them too.** A client with preferences sends the same `provider` member from `EvaluateAsync(SystemOneRequest)` as from the neutral and typed calls, or the guarantee silently does nothing there. How is the builder's call: a writer path for the raw request, or a client-side addition to its serialization. `SystemOneRequest`'s public shape does not change. *(My call, from the maintainer's "never silently do nothing".)*
- **Constraint:** the per-call allocation budgets in the AOT smoke app must not rise, including the raw `EvaluateAsync` budget, 4352 B in `AllocationChecks.cs`. The bytes are built once per client.

**Failure when OpenRouter cannot satisfy the preferences.** The call returns OpenRouter's refusal as a `DecisionError` through the existing mapping, and it is not retried.
- `StatusCode` is the status code.
- `Message` stays the generic "The API returned HTTP n.".
- OpenRouter's body stays in `Detail`, as for every other error. *(My call: putting body text in `Message` would change every error and the client's promise not to log response bodies.)*

No new `DecisionErrorKind` is added, to keep the public API small. The live check records the actual status and message. If that status would be retried, a 5xx for example, the mapping for this case is fixed so a refused routing is never retried.

**Docs.**
- `docs/providers.md`, the OpenRouter section: a "Privacy: zero data retention" subsection with an options and appsettings example. It says:
  - the client refuses to start on other providers;
  - the account-wide setting still applies in addition;
  - what the live check proved, that the endpoint reads `provider`, and what it could not prove, that `zdr` alone is enforced, because the test account is already ZDR-restricted;
  - whether `DenyDataCollection` currently blocks Jev.

  It must agree with the existing "Notes from the probes" section.
- The option's XML docs say the same.
- The README's OpenRouter line only if it already mentions privacy.

## Tests

**Unit**, in `Minos.NET.Tests` or `Minos.NET.SystemOne`'s test project, wherever the writer and validator tests already live:
- **Body bytes, neutral and typed path.** The body has no `provider` member when the preferences are null or both flags are false. It holds exactly `{"zdr":true}`, `{"data_collection":"deny"}`, or both, by flag. Everything else in the body is byte-identical to today's output, both with and without images.
- **Body bytes, raw path.** `EvaluateAsync(SystemOneRequest)` sends the same `provider` member, and nothing else changes in its body.
- **Validation.** Preferences on TypeSafe, VllmSemanticRouter and Custom fail with the message. On OpenRouter they pass. All-false preferences pass on any provider.
- **Binding.** The appsettings keys bind to both flags.
- **Errors.** A fake handler returning OpenRouter's refusal yields that `DecisionError` after one attempt, with no retry.

**Docs:** the snippet in `providers.md` compiles through mdsnippets; the existing docs tests pass.

**AOT:** add a call with preferences to the AOT smoke app if it has an OpenRouter-shaped path. Every allocation budget stays unchanged.

**Live**, in `OpenRouterLiveTests`, run by `live-smoke.yml` on `main`. The tests pin the Jev model and ignore `MINOS_LIVE_OPENROUTER_MODEL`, so another model's ZDR status cannot fail them.
1. **The endpoint reads `provider`:**
   - A request whose `provider` object cannot be satisfied, such as `{"ignore":["typesafe"]}` or `{"only":["no-such-provider"]}`, must be refused.
   - The test sends that body through the test's own HTTP path; it is not public API.
   - If OpenRouter answers normally, the endpoint ignores `provider`, and the feature must not ship as a guarantee.
2. **ZDR succeeds:** a typed call through a client with `ZeroDataRetention` alone returns an answer.
3. **`data_collection`:** a typed call with `DenyDataCollection` alone. Its outcome, an answer or a refusal, is recorded and fixes the docs line about it. Either outcome passes, but the outcome is asserted to be one of the two.

The live tests record each refusal's status and `Detail` for the docs.

## Order of work

1. Build the feature and its unit, docs and AOT tests, through a PR with a pre-push review.
2. **After the merge:** dispatch `live-smoke.yml` on `main`. The live check runs on `main`: `live-smoke.yml` is dispatched there, and the `live-api` environment is meant to deploy only from `main`.
3. **Hold the release PR**, which proposes 1.1.0, until the live run passes.
   - If check 1 shows OpenRouter ignores `provider` on `/v1/systemone`, revert the feature, or mark it as not enforced, before any release.
   - If check 3 shows `deny` refuses Jev, update the docs line before the release.
   - Then report back on #202 and to Daedalus.
4. **Close #202** with the live run's link. Daedalus can then require `ZeroDataRetention` instead of trusting `ZeroDataRetentionAccount`; that change is Daedalus's own.

## Non-goals

- Preferences per `DecisionRequest`.
- Other OpenRouter routing fields: `order`, `only`, `ignore`, `sort`, `max_price` and so on.
- Any change in Daedalus.

## Risks

| Risk | Impact | Mitigation |
|---|---|---|
| `/v1/systemone` ignores the `provider` object | A guarantee that guarantees nothing | Live check 1 runs before any release. If it fails, the feature does not ship as enforcement. |
| The endpoint reads `provider` but ignores `zdr` alone | Partly unproven guarantee | The maintainer accepted this limit, because the test account is ZDR-restricted. The docs state it. Daedalus keeps its account-level gate as well. |
| The raw path is missed | The guarantee silently does nothing for raw calls | Raw-path byte tests. |
| OpenRouter refuses `zdr` for Jev someday, for example when TypeSafe's retention changes | Calls fail | That is the intended behaviour: refusing beats leaking. The docs say so. |
| The writer change disturbs today's body bytes | Wire regression | Byte-equality tests against the current output, plus the existing conformance fixtures. |
| Allocation regression | Budget gate fails | The bytes are built once per client; the AOT smoke gates enforce it. |
