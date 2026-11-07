# Gap list: the upstream decision abstraction against a Minos-style pipeline

> **For the maintainer, not for the site.** This is a working list for the comment on
> [dotnet/extensions#7764](https://github.com/dotnet/extensions/issues/7764). It is not a docs page and is not linked
> from the site. Nothing here is posted as it stands: pick from it when writing the comment.

Checked against the experimental decision abstraction of
[#7795](https://github.com/dotnet/extensions/pull/7795) at commit `02e54f0`, the pin in `tools/upstream/decisions-pin.json`.
Every reference below is a path in the dotnet/extensions repository at that commit, with a line number. `Decisions/` is
short for `src/Libraries/Microsoft.Extensions.AI.Abstractions/Decisions/`. The item by item comparison is in
`docs/extensions-ai-decisions/mapping.md`; this list keeps only what matters for the upstream discussion.

The terms are those of the Milestone 11 design. **Layer 1** is the decision abstraction of #7795: the client
interface, requests, questions, answers and the typed binding. **Layer 2** is function and routing composition, as in
[#7796](https://github.com/dotnet/extensions/pull/7796). Upstream draws the same line in `Decisions/README.md:151`:
"AIFunction/tool interoperability is a Layer 2 concern".

Layer 1 says on purpose what it leaves out: "without applying thresholds, calibration, probability repair, retry
policy, or a joint-distribution promise" (`Decisions/README.md:3`). Several gaps below are that choice. They are listed
because a pipeline stage built on Layer 1 has to fill each of them somewhere, and the question is whether upstream
wants a shared place for them or leaves each library to invent its own.

## What Layer 1 lacks for a pipeline like Minos's

### 1. An error taxonomy, or a result type

- **Missing:** a failure is an exception with one flag. `DecisionClientException` has a message, an inner exception
  and `IsTransient`, and its summary says it classifies a failure "without prescribing retry behavior"
  (`Decisions/DecisionClient.cs:127-157`). There is no kind, status code, retry-after delay, provider request id or
  per-question failure. A protocol break is a second, unrelated exception type, `DecisionProtocolException`
  (`Decisions/DecisionAnswers.cs:361`). An invalid request throws `ArgumentException` from its constructor
  (`Decisions/DecisionQuestions.cs:233-251`), as invalid questions do from theirs.
- **Why a pipeline needs it:** a retry stage, a fallback stage and an escalation stage each decide on the kind of
  failure. Retry wants rate limiting, overload and time-outs, and the retry-after delay. Fallback wants to move on after
  a provider failure but not after the caller's own invalid question, which would fail at the next provider too.
  Escalation wants to keep the first answers when the target fails. With one flag, all of that is a guess on the
  exception's type and message. The Minos adapter maps a transient failure to `Server` and any other to `Http`, which
  names HTTP causes the exception does not report.
- **Also:** `DecisionClientException` is sealed (`Decisions/DecisionClient.cs:129`), so a library cannot add its own
  detail in a subtype that callers still catch as the upstream type. It has to wrap the detail in the inner exception.
- **Exceptions or a result:** Minos returns `Result<DecisionResponse, DecisionError>`, so a failed call is a value a
  stage routes on. Layer 1 returns `Task<DecisionResponse>` and throws (`Decisions/DecisionClient.cs:34`). Either works
  for a pipeline once the failure carries a kind. The kind is the gap; the result type is a preference.

### 2. A deadline

- **Missing:** neither the request nor the options carry a time limit. `DecisionRequest` holds the state and the
  questions only (`Decisions/DecisionQuestions.cs:224-264`). `DecisionOptions` holds the model id, extension properties
  and a raw options factory (`Decisions/DecisionClient.cs:46-82`).
- **Why a pipeline needs it:** a cancellation token stops a call but says nothing about how much time is left. An
  escalation stage needs the deadline to decide whether a second, slower decider can still answer before the caller
  gives up, and to mark the question as answered by the first decider for lack of time instead of calling at all. In
  the Minos triage run a 5-second budget meant no escalation was made: the local call took longer than that. A
  deadline in `AdditionalProperties` would work for one library, but stages from different libraries would not agree
  on the key.

### 3. A path of which decider answered

- **Missing:** a response has one provenance: one provider name, provider URI, model id, response id and precision
  (`Decisions/DecisionClient.cs:307-337`, set once per response at `:349` and `:382`). Answers have no provenance of
  their own (`Decisions/DecisionAnswers.cs:60-103`).
- **Why a pipeline needs it:** when a stage re-asks some questions of another decider and merges the answers, the
  merged response was answered by two deciders. The constructor accepts the merge, since it only needs one answer per
  question in request order (`Decisions/DecisionClient.cs:357-366`), but the response can then name only one provider
  and model. Minos reports a path: per question, who answered, why (confident, escalated, no time left, target failed,
  fell back) and both certainties, plus each call that was made with its status. Without a shared slot that ends up as
  library-specific keys in `AdditionalProperties` (`Decisions/DecisionClient.cs:398`), which a report or a caller from
  another library cannot read.

### 4. Usage and cost

- **Missing:** `DecisionResponse.Usage` is a `UsageDetails` (`Decisions/DecisionClient.cs:385`), which counts tokens
  only (`src/Libraries/Microsoft.Extensions.AI.Abstractions/UsageDetails.cs:16-42`). There is no cost, and there is one
  usage per response.
- **Why a pipeline needs it:** escalation is a trade of cost for accuracy, so the report of a run needs the cost of
  each call, and a merged response from two deciders needs the usage of each, not a sum across models priced
  differently. Hosted deciders report a cost per call; local ones report tokens. This is the weakest gap of the six:
  per-call usage fits the path of item 3, and cost may well be outside the abstraction's scope.

### 5. Capability discovery

- **Missing:** `DecisionClientMetadata` lists the supported kinds, or `null` when unknown, with the provider name, URI
  and default model (`Decisions/DecisionClient.cs:86-124`). It says nothing about limits: the most questions per
  request, the most options per choice, or whether the provider takes images.
- **Why a pipeline needs it:** a routing stage that sends unsure questions to a second decider has to know before the
  call whether that decider can take them. A provider over its question or option limit fails the whole request at run
  time, and the stage cannot tell that failure from any other, as item 1 says. Minos keeps these on
  `DecisionCapabilities`, which a client resolves for every capability. Layer 1 requests carry no images at all
  (`Decisions/DecisionQuestions.cs:261-264`), so image support is a request gap as much as a metadata gap.

### 6. Calibration metadata

- **Missing:** an answer declares how its probabilities were rounded, `HighPrecision`, `FourDecimalPlaces` or
  `TwoDecimalPlaces` (`Decisions/DecisionAnswers.cs:21-31`, on the answer at `:90`). Nothing says how the probabilities
  were made: reported by a decision model, read from token log probabilities, or stated by a chat model in text. There
  is no confidence on a choice or score answer either (`Decisions/DecisionAnswers.cs:261-296`, `:304-356`). The README
  says the probabilities carry no calibration promise (`Decisions/README.md:74`).
- **Why a pipeline needs it:** a threshold on a probability means something only for the source it was set on. A
  probability a chat model stated in text is weaker evidence than one from log probabilities, and a stage that
  escalates on a threshold should be able to tell the two apart, or at least report which it acted on. Precision is
  about rounding and cannot stand in for this. The vendors also define confidence differently, so a confidence field
  would need a stated definition; the source of the probabilities is the smaller and safer addition.

### Smaller points found while mapping

These are not topics for the comment, but they cost a pipeline stage some code:

- **Response correlation is internal.** `DecisionResponse.ValidateAgainst` checks that a response belongs to the
  request that was sent (`Decisions/DecisionClient.cs:416-433`), and the typed extensions call it
  (`Decisions/DecisionClient.cs:189`). It is `internal`, so a stage that calls `GetResponseAsync` directly and merges
  answers has to repeat the check.
- **`GetService` asks for the exact type.** The interface documents "the exact requested type and key"
  (`Decisions/DecisionClient.cs:39-41`). The chat stages answer for an instance of the requested type
  (`src/Libraries/Microsoft.Extensions.AI.Abstractions/ChatCompletion/DelegatingChatClient.cs:59`), so a base type or an
  interface finds a derived stage. For a decision pipeline with several stages the two rules give different
  answers for the same lookup.
- **The precision check is strict for rounded providers.** Under `HighPrecision` a distribution must sum to 1 within
  1e-6 (`Decisions/DecisionAnswers.cs:62`, `:136-141`, `:159-167`), and a reported score must agree with it
  (`Decisions/DecisionAnswers.cs:333-341`). A wrapper over a provider that rounds without saying how has to pick a
  precision, and a wrong pick makes the constructor throw.
- **No builder or delegating base for decision clients.** Chat has `ChatClientBuilder`
  (`src/Libraries/Microsoft.Extensions.AI/ChatCompletion/ChatClientBuilder.cs`); at the pin, decisions have no builder
  and no delegating client, so every stage library writes its own. This may be planned for a later layer.

## What overlaps Layer 2

#7796 is not in the pinned commit, so what follows compares Minos with the routing types that are there and with #7796
as its description reads.

- **Fallback overlaps failover.** Minos's `UseFallback` asks the next decider when a call fails. Upstream's
  `FailoverChatClient` does the same for chat: it selects another client after an invocation fails
  (`src/Libraries/Microsoft.Extensions.AI/ChatRouting/FailoverChatClient.cs:17-37`). If Layer 2 brings failover to
  decision clients, Minos's fallback stage is the same idea, and the names should line up: Minos says fallback,
  upstream says failover.
- **Routing before the call overlaps nothing in Minos.** `RoutingChatClient` selects one client per request and invokes
  it once (`src/Libraries/Microsoft.Extensions.AI.Abstractions/ChatRouting/RoutingChatClient.cs:19-35`). #7796 lets a
  decision pick that chat client. Minos has no stage that routes a chat request on a decision.
- **Escalation does not overlap.** Minos's `UseEscalation` re-asks only the unsure questions of a decision request and
  merges the answers by question. That is routing inside one decision request, after a successful call, on each
  answer's probability. Neither chat routing type looks at how sure a successful answer was, and #7796 routes the other
  way, from a decision to a chat client. The milestone does not implement Layer 2, and the adapters keep Minos's stages
  working over any upstream client either way.

## The narrower question of the Milestone 10 draft

The draft, `docs/plans/2026-11-06-dotnet-extensions-decision-proposal-draft.md`, asks one question: should the decision
abstraction define how to escalate one question at a time on its probability, and how to report which decider
answered each question, or should that stay in libraries above it?

At the pin the answer is the second, by design: Layer 1 applies no thresholds and no retry policy
(`Decisions/README.md:3`) and defines no routing composition (`Decisions/README.md:151`). That leaves the question
smaller than the six gaps above. A library can escalate over Layer 1 today. What it cannot do in a shared way is:

- report the result, because the response has one provenance and one usage (item 3, item 4);
- tell a provider failure that should leave the first answers in place from a request the target could never take
  (item 1, item 5);
- respect the caller's time limit across stages from different libraries (item 2);
- say what kind of probability the threshold acted on (item 6).

So the comment can ask the narrow question and name these as the parts that would have to be in Layer 1 for
escalation to stay out of it: a per-question provenance or path slot on the response, an error kind, a deadline, and
the source of the probabilities. The draft's five open questions already cover the path, the error result and the
probability source; the deadline and the capability limits are the two this list adds.
