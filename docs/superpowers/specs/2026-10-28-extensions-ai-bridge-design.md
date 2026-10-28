# Design — `Minos.NET.Extensions.AI`: an `IDecisionClient` over any `IChatClient`

**Date:** 2026-10-28
**Surface:** `Backend`
**Stage:** feature (one package plus a small core addition, implementation-ready)

> Planned in Phase 6.3's design (`2026-10-10-phase-6.3-decision-client-pipeline-design.md`): "A later
> `Minos.NET.Extensions.AI` package bridges to `IChatClient`." Needed by the incident-triage demo (its roadmap,
> Milestone 3), which runs online on OpenRouter.

## Intent

Minos today reaches decision providers that speak System One or OpenAI's Decisions API. Many teams already have an
`IChatClient` configured, for OpenAI, OpenRouter, Azure, Ollama or anything else Microsoft.Extensions.AI wraps. This
package lets them ask Minos questions over that client, with the same typed question sets, pipeline stages, caching
and recording they get from the other providers.

A chat model is not a decision model: it does not return calibrated per-option probabilities. The bridge gets the most
honest numbers it can and **says how it got them**, so no caller mistakes a model's stated guess for a measured
distribution. Showing invented confidence as if it were calibrated is the failure this design exists to avoid.

## Goals

- `ChatDecisionClient : IDecisionClient` over any `IChatClient`, answering Noul, Choice and Score questions.
- Probabilities from **logprobs** where the model returns them; otherwise **verbalized** by the model in
  schema-bound JSON. Every response states which.
- Works with the existing pipeline (`DecisionClientBuilder`, `UseRecording`, `UseCaching`, retries, telemetry) and
  with `Minos.NET.DependencyInjection`.
- Passes the conformance suite through a new chat-completions wire, with a fixture recorded live against OpenRouter.
- `net10.0`, Native AOT clean: zero IL2 and IL3 warnings, source-generated JSON only, included in `aot-smoke` and
  `aot-surface`.
- A docs page and a sample. Released on nuget.org through release-please before **Saturday 31 October 2026, 18:00**.

## Non-goals

- Sampling-based probabilities, N calls at temperature 1. Rejected: N times the cost and latency, steps of 1/N.
- Calibrating chat-model output, for example temperature scaling on a dataset. A later Evaluation feature may do this.
- Logprob extraction for providers other than the OpenAI SDK's `ChatCompletion`. Other clients use the verbalized path.
- Reasoning models on the logprob path. Their first output token is not the answer; they get the verbalized path.
- Model listing. `SupportsModelListing` is off.

## Anti-goals

- A verbalized probability that reaches a caller, the cache or a decision record without its mark.
- A bridge that silently returns numbers the conformance checks would reject: non-finite values, probabilities that do
  not sum to 1, or an answer missing for a question asked.
- Request state in logs, traces or exception messages; the same rule as the rest of Minos.

## Decisions

By the maintainer, 2026-10-28:

1. **Probabilities: logprobs first, verbalized JSON as fallback.** When the response carries top-logprobs for the answer
   token, the bridge takes a softmax over the option labels' logprobs. Otherwise the model states each option's
   probability in JSON, and the bridge normalizes them.
2. **The mark is a core property, per answer, with a summary per response.** The core gets a public enum, working name
   `ProbabilitySource`, with `Model`, `Logprobs` and `Verbalized`. In `Auto` one question can be answered by logprobs
   and another verbalized, so the mark sits on each answer (2026-10-28).
   - `QuestionAnswer` carries its source. The typed `Noul`, `Choice<T>` and `Score<T>` expose it as a
     `ProbabilitySource` property, so a typed caller sees it without the raw response. The generator's output passes it
     through.
   - `DecisionResponse.ProbabilitySource` is the weakest source among its answers: `Verbalized`, then `Logprobs`, then
     `Model`.
   - Every existing client reports `Model`, and so do the existing factory methods and constructors by default. New
     overloads take the source.
   - `CachedCopy` and the distributed cache format in `Minos.NET.Caching` carry it. A cache entry written before this
     version reads as `Model`.
   - `Minos.NET.Recording` writes it per answer into the decision record, and a record without it reads as `Model`.
   - It is a public API addition in the core, released as a minor version.
3. **Conformance gets a new wire, `ConformanceWire.ChatCompletions`** (scope settled 2026-10-28):
   - Fixtures can hold several exchanges per probe, one per question. Replay matches each request to its exchange by
     the question it asks, not by arrival order, because the calls run in parallel.
   - The conformance target runs the bridge in `ProbabilityMode.Logprobs`, so a probe never adds a fallback exchange.
   - S3, D1 to D4, M1, R1 to R3 and E2 run on what Minos read. S1 and S2 read a System One or Decisions answer body,
     which a chat completion does not have, so on this wire they skip with that reason. E1 is settled under My calls.
     `conformance.md` documents all of this.
   - The first fixture is marked source `tests`, built from the bridge's unit-test responses, so the release does not
     wait on a live key.
   - After the merge, the live-smoke workflow is dispatched on `main` to record the OpenRouter fixture. The recording
     comes in through a follow-up PR and must show the `Logprobs` path.
     - Live-smoke already holds `OPENROUTER_API_KEY`, and it gains the chat-completions recording.
     - If the chosen model returns no logprobs through OpenRouter, another non-reasoning model that does is chosen, and
       the fixture names it.
4. **Logprobs are read from the OpenAI SDK's raw response.** The package references the `OpenAI` package and
   `Microsoft.Extensions.AI.OpenAI`.
   - It sets `IncludeLogProbabilities` and `TopLogProbabilityCount` through `ChatOptions.RawRepresentationFactory`.
   - It reads `ChatCompletion.ContentTokenLogProbabilities` from `ChatResponse.RawRepresentation`.
   - That covers OpenAI, and OpenRouter through the OpenAI client. Any other raw representation takes the verbalized
     path.
5. **One chat call per question, run in parallel.** The answer is the first output token, so its logprobs sit at
   position 0 and no token positions inside JSON need finding. Usage is summed across the calls.
6. **Confidence is the picked option's probability**, on both paths, for Choice and Score. A Noul keeps no confidence,
   as everywhere else in Minos.
7. **Every question kind: Noul, Choice and Score.** A Score uses the Choice path over its levels. `Expected` is the sum
   of each level index times its probability, which is check D3.
8. **Registration has two forms:**
   - `chatClient.AsDecisionClient(options)`, named after Microsoft.Extensions.AI's `AsIChatClient`;
   - `services.AddChatDecisionClient(...)`, which resolves `IChatClient` from the container and returns the DI
     builder, so `.UseRecording()` and the other stages chain as they do after `AddSystemOneDecisionClient`. A named
     overload resolves a keyed `IChatClient` and registers a keyed decision client, matching `AddDecisionClient(name,
     factory)`.

My calls (Claude's), open to correction:

- **Names.** The package is `Minos.NET.Extensions.AI` and the namespace `Minos.Extensions.AI`. The types are
  `ChatDecisionClient`, `ChatDecisionClientOptions` and `ProbabilityMode`. DI lives in
  `Microsoft.Extensions.DependencyInjection`, as the other packages' DI does.
- **`ProbabilityMode` option: `Auto`, the default, then `Logprobs` and `Verbalized`.**
  - `Auto` asks for logprobs first. When a response carries none, it re-asks that question verbalized.
  - It keeps no memory between calls, so one odd response never silently moves every later call onto the verbalized
    path.
  - The cost is an extra call per question on a model without logprobs. The docs say to set `Verbalized` for such a
    model.
  - `Logprobs` returns `Unsupported` instead of falling back. `Verbalized` never asks for logprobs.
- **Native AOT.** Zero IL2 and IL3 warnings in `aot-smoke` publish, the new package's dependencies included. The first
  plan task is a spike that publishes a minimal AOT app over `AsIChatClient().AsDecisionClient()`.
  - The verbalized schema is built by hand as a `JsonElement`, not with `AIJsonUtilities`.
  - If a dependency warns, the warning is not suppressed: the cause is found and fixed or reported upstream, and the
    maintainer is told before going further.
- **Labels.** Options and levels are labelled `A`, `B`, `C` and so on, and a Noul is `A` for yes and `B` for no.
  - The labels are single uppercase letters, so most tokenizers give each its own token.
  - The ask is capped at 26 options, which sets the default `MaxChoiceOptions` to 26 (MIN203 above that).
  - A Noul's probability is the softmax over the logprobs of `A` and `B`.
- **Matching logprob tokens.** A top-logprob token counts for a label when, trimmed of whitespace, it equals the label
  exactly.
  - Several matching tokens for one label, such as `A` and ` A`, are summed in probability space.
  - The logprob request is `MaxOutputTokens = 1`, `Temperature = 0` and `TopLogProbabilityCount = 20`, the most
    OpenAI allows.
  - A label missing from the top 20 gets the leftover mass, `1 − sum(found)`, shared evenly among the missing labels.
    Then everything is normalized.
  - When no label at all appears in the top 20, that is a malformed answer. In `Auto` the question is re-asked
    verbalized; in `Logprobs` it returns an error.
- **The verbalized path** uses `GetResponseAsync` with a JSON schema response format.
  - The schema is built per question: an object with one number per label, from 0 to 1.
  - It is parsed with a source-generated `JsonSerializerContext`.
  - Values that are not finite or are negative are a malformed response. A sum of zero is too. Otherwise the values
    are normalized to sum to 1.
- **The prompt.**
  - A system message says the model is a classifier answering one question about the state.
  - The user message holds the question's text, then the criteria or options with their labels, descriptions,
    examples and `NotFor`, then the state.
  - A Noul's `WhenTrue` and `WhenFalse` are sent; images go as `DataContent` parts.
  - The templates are internal constants, not configurable in this release.
- **Capabilities default:** every kind, no `MaxQuestions`, `MaxChoiceOptions = 26`, `SupportsImages = false` (true
  only when the user sets it, as the OpenRouter preset does), `SupportsModelListing = false`.
- **Errors.** Exceptions from the `IChatClient` map to `DecisionError` through a small mapper:
  - `ClientResultException` from the OpenAI SDK maps by status, as the existing HTTP mapper does: 400 and 422 are
    `Validation`, 401 and 403 `Unauthorized`, 429 `RateLimited` with `Retry-After` read, and 5xx `Server`;
  - `HttpRequestException` is `Network`;
  - a timeout is `Timeout`;
  - anything else is `Unknown` with the exception kept as the inner error, never the request state;
  - one failed question fails the whole call, and the other calls are cancelled.
- **Metadata.**
  - `GetService(typeof(DecisionClientMetadata))` returns the inner client's `ChatClientMetadata` mapped across:
    provider name, endpoint and default model.
  - `GetService` also forwards to the inner `IChatClient`, as `DelegatingChatClient` does.
  - `DecisionResponse.Model` is the `ModelId` the chat response reports.
- **Retries.** The bridge itself does not retry. The pipeline's `UseRetries` stage does, as with the other clients.
- **Conformance target's factory.**
  - It builds the OpenAI SDK `ChatClient` over the runner's `HttpClient`, through `HttpClientPipelineTransport`, with
    the base address `https://openrouter.ai/api/v1`, then wraps it with `AsIChatClient().AsDecisionClient()`.
  - The `invalid` probe is a request with no questions. The bridge answers it with `Validation` before anything is
    sent, so on this wire E1 checks that `Validation` result and its message, not an HTTP status.
  - The default model for the recorded fixture is a non-reasoning model on OpenRouter that returns logprobs, such as
    `openai/gpt-4.1-mini`. The fixture names the model and the date.
- **Release wiring.** CI's package lists go from eight to nine library packages: the `aot-surface` list, the
  `pack-validate` inspection list and the publish step's id list, and `RELEASING.md` updates its count.
  - Trusted Publishing's `*` glob already covers the new id, and the `Minos.NET` prefix is reserved, so no new
    nuget.org setting is needed. This is verified after the release.
- **Sample:** `samples/Minos.NET.Samples.ChatClient`. It answers a small question set over an `IChatClient`, with
  `UseRecording`, and prints each answer with its `ProbabilitySource`. It reads `OPENROUTER_API_KEY`, and without it
  falls back to a scripted fake `IChatClient`, so it runs in CI.
- **Docs page:** `docs/chat-clients.md`, linked from `providers.md`'s at-a-glance table as a new column and from
  `choosing-a-provider.md`. It states plainly what `Verbalized` means and that bridge confidence is not TypeSafe's
  calibrated confidence. Snippets come from the docs tests, as the other pages' do.

## Constraints

- Conventional commits. The package commit is `feat:`. No nested parentheses in commit message bodies, because
  release-please drops the commit. No Claude signatures, trailers or session links.
- A feature branch and a PR; the maintainer merges. Builds and tests run one at a time: the VM's memory is shared.
- `TreatWarningsAsErrors`, PublicAPI tracking (RS0016 and RS0017), and the analyzers as configured.
- After the merge, confirm the commit is in the release-please PR. After the release, check the package on nuget.org.

## Left to the builder

- Internal structure: the prompt builder, the logprob reader, the verbalized reader and the error mapper.
- Test layout. TDD with a scripted fake `IChatClient` that returns hand-built `ChatCompletion` raw representations,
  made with `OpenAIChatModelFactory`, for the logprob path.
- The exact `ProbabilitySource` name, if a clearer one turns up during the API review, as long as the three values keep
  their meaning.
