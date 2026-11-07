# Draft: escalating unsure decisions, for dotnet/extensions

> **For the maintainer, not part of the post.** Open proposals already ask the question this draft was planned around.
> [#7764](https://github.com/dotnet/extensions/issues/7764) proposes an `IDecisionClient` for Microsoft.Extensions.AI,
> and [#7795](https://github.com/dotnet/extensions/pull/7795) implements it as an experimental API. Both were open on
> 2026-11-07. So the draft is written as a comment on #7764, not as a new discussion. Everything below the line is the
> post. Remove this note before posting.

---

## Summary

[#7764](https://github.com/dotnet/extensions/issues/7764) proposes a decision primitive for Microsoft.Extensions.AI. A
comment on that issue on 2026-09-30 says "it makes sense for MEAI to expose an experimental API to support
these capabilities", and [#7795](https://github.com/dotnet/extensions/pull/7795) proposes one. This comment adds data
from a separate implementation, and asks one follow-up question, with five details of it at the end.

[Minos.NET](https://github.com/MarcelRoozekrans/Minos.NET) is an open-source .NET library that has its own
`IDecisionClient`. It also has a pipeline stage that routes on the answer: a cheap decider answers first, and only the
questions it was unsure of go to a stronger decider. We measured this on one dataset.

**The question:** should the decision abstraction in Microsoft.Extensions.AI define how to escalate one question at a
time on its probability, and how to report which decider answered each question? Or should that stay in libraries
above the abstraction?

We are not asking you to adopt Minos's names. The API sketch at the end is there to show the shape, not to propose it.

## The gap

`IChatClient` returns a `ChatResponse`: messages and their text, plus metadata such as `ModelId`, `FinishReason` and
`Usage`. `GetResponseAsync<T>` asks for structured output and parses the reply into `T`. So an app can get a
classification as an enum from a chat model.

It cannot get a probability for that classification. Neither `ChatResponse` nor `ChatOptions` has a property for a
probability, a confidence or logprobs. So Minos's bridge from `IChatClient` to its decision client reads logprobs
from the OpenAI SDK's `ChatCompletion` under the chat response. That works for OpenAI and for OpenAI-compatible servers
reached through the OpenAI client. A chat client that wraps another SDK gives no logprobs. The bridge then asks the
model to state the probabilities as text, and marks those answers as verbalized, which is weaker evidence.

The routing that exists today does not help with this. `RoutingChatClient` picks one client before the call.
`FailoverChatClient` picks another after a call fails. Both are experimental (`MEAI001`). Neither looks at how sure a
successful answer was. [#7712](https://github.com/dotnet/extensions/issues/7712) proposes a `CascadingChatClient` that
moves to the next client when the app does not accept a successful response. It leaves the quality test to the app,
and it works on a whole chat response.

A decision response is different. It answers several questions at once, and each answer has its own probability. A
cheap decider can be sure of two questions and unsure of the third. Escalating the whole request repeats the two it got
right. Escalating only the third needs a re-ask of one question, a merge of the answers by question, and a record of
which decider answered which question. [#7796](https://github.com/dotnet/extensions/pull/7796) composes decisions with
`RoutingChatClient`, so a decision picks which chat client gets a chat request. Escalation is the other way round: it
re-asks the unsure questions of a decision request. #7795 says of itself: "No implicit threshold, normalization,
clamping, retry, fallback, joint distribution, or one-physical-network-call promise is introduced." We have not found
per-question escalation in #7764, #7795, #7796 or #7712.

## Prior art

These are the claims we could check on the vendors' public pages on 2026-11-07. Each page is listed under Sources.

- **OpenAI** says on its Decisions guide that "The Decisions API is now generally available." Its question types are
  `predicate`, `choice` and `score`. "Predicates return the estimated probability that a condition is true." "Choice
  and score answers return a probability distribution and a separate `confidence` field." The guide says to "Use
  labeled examples from your application to set thresholds for routing, filtering, or review."
- **TypeSafe** describes Jev as a model that "evaluates typed questions against a state and returns structured results
  directly", with question types including Noul, Choice and Score. Its confidence page says that "`confidence` is a
  statistic computed from the probability distribution the answer already gives you", and that "Confidence measures
  how concentrated the answer's probabilities are". It calls its formula "one reasonable way to summarize a
  distribution, not the only one."
- **Cloudflare** describes clef-flash as a model that "turns a state and a schema of typed questions into decisions"
  and "returns a probability for every allowed option of every question". Its question types are `noul`, `choice` and
  `score`.

None of the three pages says that its probabilities are calibrated. OpenAI's guide tells you to set thresholds from
your own labelled examples. Minos's evaluation tools measure calibration on labelled data, so that a threshold can be
set from a report rather than guessed.

## Usage

The app registers a local decider as the default client and a hosted one under a key. `AddSystemOneDecisionClient` is
Minos's client for the wire protocol that both deciders speak. Escalation sends a question to the hosted decider when
the local answer is below its threshold. The thresholds are per question key, with a default for the rest.
The values below are the ones the measured run used.

```cs
// A question the local model is less than 0.6 sure of is asked again of "hosted", unless it has a threshold of its
// own. The keys are the question keys of the Banking77 set, and a key the set does not have is ignored.
public static IServiceCollection AddTriage(IServiceCollection services, string localKey, string hostedKey)
{
    services.AddSystemOneDecisionClient("hosted", options => options.ApiKey = hostedKey);
    services.AddSystemOneDecisionClient(options => options.ApiKey = localKey)
        .UseEscalation("hosted", options =>
        {
            options.PrimaryName = "local";
            options.DefaultThreshold = 0.6;
            options.Thresholds["intent"] = 0.85;
            options.Thresholds["card_not_arrived"] = 0.80;
            options.Thresholds["about_a_payment"] = 0.80;
        });
    return services;
}
```

A Choice or Score question counts as unsure when its confidence is below the threshold. A yes/no question counts as
unsure when max(p, 1 − p) is below it, so p = 0.05 is as sure as p = 0.95.

The target can be any decision client, including one over an `IChatClient`:

```cs
// chat is any IChatClient. The unsure questions of primary are asked again of it, under the name "chat". The merged
// response reports the weakest source among all its answers, so Verbalized means at least one answer is the model's
// own estimate. Disposing client disposes primary, and target is disposed here too. It leaves chat open.
public static async Task<ProbabilitySource?> SourceAsync(
    IChatClient chat, IDecisionClient primary, QuestionSetDefinition definition, string message, CancellationToken cancellationToken)
{
    using var target = new ChatDecisionClient(chat);
    using var client = primary.AsBuilder().UseEscalation(target, options => options.TargetName = "chat").Build();
    var result = await client.EvaluateAsync(new DecisionRequest(definition, DecisionContent.FromString(message)), cancellationToken);
    return result.IsFailure ? null : result.Value.ProbabilitySource;
}
```

What the stage does, as built:

- It asks the target only the unsure questions, with the same state, and merges the answers by question key. A
  question the first decider was sure of is never re-asked.
- The escalated answer replaces the first one, even when the target is less sure. The app chose the target as the
  stronger decider. The record keeps both certainties, so a report can show where they disagreed.
- It calls the target only when there is time. The deadline is the request's `Deadline`, or a `Budget` counted from
  the start of the stage. With less time left than the target's `ExpectedLatency`, the unsure questions keep the first
  answer and are marked `NoTimeLeft`. The deadline does not cut the first decider's own call, unless a fallback stage
  inside escalation cuts it: that stage cuts its first client at the deadline minus the smallest later `ExpectedLatency`.
  Otherwise it bounds only the escalation.
- A target that fails leaves the first answers in place, marked `TargetFailed`. The call still succeeds.
- `DecisionResponse.Path` lists, for each question, who answered and why, and each call that was made. `Path` is null
  from a pipeline without either stage, and from a fallback stage alone when its first client answered, so read it as
  nullable.
- `ProbabilitySource` on the response is the weakest source across all its answers.

A separate `UseFallback` stage handles failure: when a client fails with a provider error, the next one in a chain is
asked. Escalation is for doubt, fallback for failure, and escalation sits outside fallback.

## Evidence

All figures are from committed reports in the Minos.NET repository. The traffic was replayed from a Banking77-based
set of 400 tickets, not taken from production. Each ticket has three questions: one Choice with ten options and two
yes/no questions, so 1,200 questions. The local decider was a small model, `Mapika/decider-2b-GGUF` at Q4_K_M, served
on a GitHub-hosted CPU runner. The hosted decider was TypeSafe's `jev-1.14.0`.

This is one dataset, one local model on CPU and one hosted decider. It shows what happened on that run. It is not a
claim that every app should escalate.

**The triage run** ([report](https://github.com/MarcelRoozekrans/Minos.NET/tree/main/docs/reports/triage/banking77)),
with a budget of 15 seconds per ticket:

| Measure | Value |
| --- | --- |
| Questions answered by the local model and kept | 1,094 of 1,200 |
| Questions escalated | 106 of 1,200, in 104 target calls, one per ticket that had an unsure question |
| Accuracy of the local answer alone, over the 1,200 questions | 0.9267 |
| Accuracy of the final answer, over the 1,200 questions | 0.9308 |
| Escalated questions the target fixed or broke | 10 fixed, 5 broken |
| Median local call / median target call | 5282.9 ms / 102.3 ms |
| Cost per 1,000 tickets, in the currency of the price table | 0.00597733 |

Two more results from the same report. First, the threshold flagged most wrong Choice answers and almost no wrong
yes/no answers: it caught 31 of the 37 wrong Choice answers and 1 of the 51 wrong yes/no answers. A confident wrong
answer is never escalated. Second, time decides whether escalation runs at all. An earlier run with the sample's
default budget of 5 seconds made no target call: the local call took a median of 5.5 s, so all 106 questions below their
threshold ended as no time left, and nothing was billed.

**The shadow run** ([report](https://github.com/MarcelRoozekrans/Minos.NET/tree/main/docs/reports/shadow/banking77)) sent
the same 400 tickets to the hosted decider and, in the background, to the local model:

| Measure | Hosted | Local |
| --- | --- | --- |
| Median latency per request | 50.5 ms | 4043.8 ms |
| Expected calibration error, 10 equal-count bins, over the 1,200 questions | 0.0262 | 0.0510 |

On the Choice question the two agreed on 0.9600 of the 400 tickets.

## API sketch

Minos's shipped signatures, simplified to C#. `Result<TValue, TError>` is from the ZeroAlloc.Results package. The two
stages are optional: nothing escalates or falls back unless the app adds the stage.

<details><summary>API sketch</summary>

```cs
// The abstraction.
public interface IDecisionClient : IDisposable
{
    ValueTask<Result<DecisionResponse, DecisionError>> EvaluateAsync(DecisionRequest request, CancellationToken cancellationToken = default);
    object? GetService(Type serviceType, object? serviceKey = null);
}

public sealed record DecisionRequest
{
    public DecisionRequest(QuestionSetDefinition definition, DecisionContent state);
    public QuestionSetDefinition Definition { get; }
    public DecisionContent State { get; }
    public IReadOnlyList<DecisionImage> Images { get; init; }
    public string? Model { get; init; }
    public int RetryAttempt { get; init; }
    public DateTimeOffset? Deadline { get; init; }
}

public sealed class DecisionResponse
{
    public DecisionResponse(QuestionSetDefinition definition, IReadOnlyList<QuestionAnswer> answers, string? model = null, DecisionUsage? usage = null);
    public QuestionSetDefinition Definition { get; }
    public QuestionAnswerList Answers { get; }
    public string? Model { get; }
    public DecisionUsage? Usage { get; }
    public string? Id { get; init; }
    public bool IsCached { get; init; }
    public string? UpstreamProvider { get; init; }
    public ProbabilitySource ProbabilitySource { get; }
    public DecisionPath? Path { get; init; }
    public T As<T>();
}

public enum ProbabilitySource { Model = 0, Logprobs = 1, Verbalized = 2 }

public sealed class DecisionError
{
    public DecisionError(DecisionErrorKind kind, string message);
    public DecisionError(DecisionErrorKind kind, string message, IReadOnlyList<QuestionFailure> failures);
    public DecisionErrorKind Kind { get; }
    public string Message { get; }
    public IReadOnlyList<QuestionFailure> Failures { get; }
    public int? StatusCode { get; init; }
    public TimeSpan? RetryAfter { get; init; }
    public string? RequestId { get; init; }
    public JsonElement? Detail { get; init; }
    public Exception? Exception { get; init; }
}

public enum DecisionErrorKind
{
    Unauthorized = 1, Validation = 2, RateLimited = 3, Overloaded = 4, Server = 5, Http = 6, Network = 7,
    Timeout = 8, InvalidResponse = 9, Unsupported = 10, InvalidQuestions = 11, Disposed = 12, Refused = 13,
}

// The pipeline builder.
public class DecisionClientBuilder
{
    public DecisionClientBuilder(IDecisionClient innerClient);
    public DecisionClientBuilder(Func<IServiceProvider, IDecisionClient> innerClientFactory);
    public DecisionClientBuilder Use(Func<IDecisionClient, IDecisionClient> stageFactory);
    public DecisionClientBuilder Use(Func<IDecisionClient, IServiceProvider, IDecisionClient> stageFactory);
    public DecisionClientBuilder Use(Func<DecisionRequest, IDecisionClient, CancellationToken, ValueTask<Result<DecisionResponse, DecisionError>>> evaluate);
    public IDecisionClient Build(IServiceProvider? services = null);
}

public static DecisionClientBuilder AsBuilder(this IDecisionClient innerClient);

// Optional stage: escalation, for doubt.
public static DecisionClientBuilder UseEscalation(this DecisionClientBuilder builder, IDecisionClient target, Action<EscalationOptions> configure);
public static DecisionClientBuilder UseEscalation(this DecisionClientBuilder builder, Func<IServiceProvider, IDecisionClient> target, Action<EscalationOptions> configure);

// In Minos.NET.DependencyInjection: the target is a client registered under a key.
public static DecisionClientBuilder UseEscalation(this DecisionClientBuilder builder, string targetKey, Action<EscalationOptions>? configure = null);

public sealed class EscalationOptions
{
    public double DefaultThreshold { get; set; }               // 0.6
    public IDictionary<string, double> Thresholds { get; }     // per question key
    public TimeSpan ExpectedLatency { get; set; }              // of the target
    public TimeSpan? Budget { get; set; }                      // when the request has no Deadline
    public string PrimaryName { get; set; }                    // "primary"
    public string? TargetName { get; set; }
}

// Optional stage: fallback, for failure.
public static DecisionClientBuilder UseFallback(this DecisionClientBuilder builder, Action<FallbackOptions> configure);
public static DecisionClientBuilder UseFallback(this DecisionClientBuilder builder, Func<IServiceProvider, FallbackOptions> configure);

public sealed class FallbackOptions
{
    public IList<FallbackStep> Steps { get; }
    public Func<DecisionError, bool> ShouldFallBack { get; set; }
    public TimeSpan? Budget { get; set; }
    public string PrimaryName { get; set; }                    // "primary"
}

public sealed class FallbackStep
{
    public FallbackStep(string name, IDecisionClient client);
    public string Name { get; }
    public IDecisionClient Client { get; }
    public TimeSpan ExpectedLatency { get; init; }
}

// The path: who answered each question, and each call made.
public sealed class DecisionPath
{
    public DecisionPath(IReadOnlyList<QuestionRoute> routes, IReadOnlyList<DecisionCall> calls);
    public IReadOnlyList<QuestionRoute> Routes { get; }
    public IReadOnlyList<DecisionCall> Calls { get; }
}

public readonly struct QuestionRoute
{
    public QuestionRoute(string key, string answeredBy, RouteReason reason, double primaryCertainty, double? targetCertainty);
    public string Key { get; }
    public string AnsweredBy { get; }
    public RouteReason Reason { get; }
    public double PrimaryCertainty { get; }
    public double? TargetCertainty { get; }
}

public enum RouteReason { Confident = 0, Escalated = 1, NoTimeLeft = 2, TargetFailed = 3, FellBack = 4 }

public sealed record DecisionCall(string ClientName, string? Model, DecisionUsage? Usage, bool IsCached)
{
    public CallStatus Status { get; init; }
    public DecisionErrorKind? ErrorKind { get; init; }
}

public enum CallStatus { Answered = 0, Failed = 1, NotCalledNoTimeLeft = 2 }
```

</details>

## Open questions for the maintainers

1. **Package placement.** If escalation belongs in Microsoft.Extensions.AI, is it a stage in `Microsoft.Extensions.AI`
   next to the caching and telemetry stages, with only the path types in the abstractions package? Or does it belong
   in a separate package, or outside dotnet/extensions?
2. **Naming.** Minos says escalation for doubt and fallback for failure. #7712 says cascading, and the experimental
   routing types say failover. One vocabulary across chat and decisions would help users.
3. **How probability is expressed.** The vendors define confidence differently, and none of the pages above claims
   calibration. Should the abstraction say where a probability came from, as Minos's `ProbabilitySource` does, so a
   threshold can tell a measured probability from a verbalized one? And should an escalation threshold act on the
   probability or on a confidence?
4. **An error result or exceptions.** Minos returns `Result<DecisionResponse, DecisionError>`, so a failed target is a
   value the stage can route on. #7795 uses `Task<DecisionResponse>` and a `DecisionClientException`. Either works for
   escalation. Which one should a stage built on the abstraction expect?
5. **How the path is reported.** Minos puts the path on the response, per question, with both certainties and each
   call. Would it rather be a property on the response, an entry in `AdditionalProperties`, or only telemetry?

## Sources

Checked on 2026-11-07.

Microsoft.Extensions.AI and dotnet/extensions:

- https://learn.microsoft.com/dotnet/ai/microsoft-extensions-ai: the libraries and their interfaces.
- https://learn.microsoft.com/dotnet/ai/ichatclient: `GetResponseAsync`, the pipeline stages, and chat routing.
- https://learn.microsoft.com/dotnet/ai/quickstarts/structured-output: `GetResponseAsync<T>`.
- https://learn.microsoft.com/dotnet/api/microsoft.extensions.ai: the namespace has no decision type.
- https://learn.microsoft.com/dotnet/api/microsoft.extensions.ai.chatresponse: the properties of `ChatResponse`.
- https://learn.microsoft.com/dotnet/api/microsoft.extensions.ai.chatoptions: the properties of `ChatOptions`.
- https://learn.microsoft.com/dotnet/api/microsoft.extensions.ai.routingchatclient: selects one client per request.
- https://learn.microsoft.com/dotnet/api/microsoft.extensions.ai.failoverchatclient: selects another after a failure.
- https://github.com/dotnet/extensions/issues/7764: the decision abstraction proposal.
- https://github.com/dotnet/extensions/pull/7795: the experimental decision abstractions.
- https://github.com/dotnet/extensions/pull/7796: decisions with `AIFunction` and `RoutingChatClient`.
- https://github.com/dotnet/extensions/issues/7712: the `CascadingChatClient` proposal.

Vendors:

- https://developers.openai.com/api/docs/guides/decisions: OpenAI's Decisions guide.
- https://docs.typesafe.ai: TypeSafe's overview of Jev.
- https://docs.typesafe.ai/confidence: TypeSafe's definition of confidence.
- https://developers.cloudflare.com/workers-ai/models/clef-flash/: Cloudflare's clef-flash model page.

Minos.NET:

- https://github.com/MarcelRoozekrans/Minos.NET: the library.
- https://marcelroozekrans.github.io/Minos.NET/: its documentation, with the Escalation guide.
- https://github.com/MarcelRoozekrans/Minos.NET/tree/main/docs/reports/triage/banking77: the triage report.
- https://github.com/MarcelRoozekrans/Minos.NET/tree/main/docs/reports/shadow/banking77: the shadow report.
