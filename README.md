# ZeroAlloc.Jev

[![CI](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/actions/workflows/ci.yml/badge.svg)](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/actions/workflows/ci.yml)

Unofficial .NET client for [TypeSafe AI](https://typesafe.ai)'s **Jev**, the first System One model: send a `state` and typed questions (Noul, Choice, Score) and get calibrated, typed answers back — directly from TypeSafe or through OpenRouter.

> **Not affiliated with TypeSafe AI.** ZeroAlloc.Jev is a community project in the [ZeroAlloc](https://github.com/ZeroAlloc-Net) family. TypeSafe publishes official SDKs for Python and JavaScript; see [docs.typesafe.ai](https://docs.typesafe.ai).

**Status:** early development, not yet published to NuGet. See [the roadmap](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/blob/main/docs/planning/ROADMAP.md).

## Requirements

.NET 10 SDK 10.0.100 or later to use the package. Building this repository needs the SDK version pinned in `global.json` (10.0.401 or later, via `rollForward: latestMinor`). The `[JevQuestions]` generator compiles against Roslyn 5.0, so builds and IDEs must host Roslyn 5.0 or later — Visual Studio 2026 version 18.0 is the first release that qualifies. An older IDE shows generator errors even when `dotnet build` succeeds.

## What works so far

- `JevClient` calls `POST /v1/systemone` and `GET /v1/models` on TypeSafe or OpenRouter and returns `Result<T, JevError>` for every outcome — HTTP errors, network failures, time-outs and unreadable responses included.
- `[JevQuestions]` source generator: declare questions as a C# type; the question JSON is emitted at compile time and answers parse into `Noul`, `Choice<TEnum>` and `Score<TEnum>`.
- Typed evaluation: `EvaluateAsync<T>` and `EvaluateAsync<T, TState>` return the generated type directly, over a raw, pooled-buffer path.
- Analyzers and code fixes: question sets are checked at compile time against the Jev API's rules and the generator's, with code fixes that add a missing `[Criteria]` or `[Level]` description. See [Diagnostics](#diagnostics).
- Native AOT: no reflection, verified by an AOT smoke app in CI.

## Example

```csharp
using ZeroAlloc.Jev;

using var jev = new JevClient(new JevClientOptions { ApiKey = apiKey }); // or set TYPESAFE_API_KEY

var result = await jev.EvaluateAsync(new SystemOneRequest
{
    State = "Help! My payouts have been failing for 3 days.",
    Questions = new Dictionary<string, JevQuestion>
    {
        ["is_urgent"] = new NoulQuestion { Instructions = "Does this convey urgency?" },
    },
});

if (result.IsSuccess && result.Value.Answers["is_urgent"] is NoulAnswer urgent)
{
    Console.WriteLine($"P(urgent) = {urgent.Noul:P0}");
}
else if (result.IsFailure)
{
    Console.WriteLine($"{result.Error.Kind}: {result.Error.Message}");
}
```

For OpenRouter, set `Provider = JevProvider.OpenRouter` and an OpenRouter key (or `OPENROUTER_API_KEY`).

### Retries

Rate limiting (429), overload (503, 529), other server errors (5xx), request time-outs (408), network failures and client time-outs are retried with exponential backoff, honouring the server's `Retry-After`. Defaults follow TypeSafe's official SDKs: 2 retries, a 500 ms initial backoff and jitter; each wait, including a server's `Retry-After`, is capped at 30 s. Tune them with `MaxRetries`, `InitialBackoff`, `MaxRetryDelay` and `Jitter` on `JevClientOptions`. Retrying a time-out, network failure or 5xx can process, and bill, a request twice; set `MaxRetries = 0` where that matters. If you pass your own `HttpClient` that already has a retry handler, such as `AddStandardResilienceHandler`, set `MaxRetries = 0` so retries don't multiply. Each retry sends the attempt number as `X-TypeSafe-Retry-Count`, as TypeSafe's official SDKs do.

## Typed evaluation

Declare the questions as a partial record with `[JevQuestions]`; the generator emits the question JSON at compile time and a `Parse` method that reads the typed answers. Call `EvaluateAsync<T>` with the state, and match on the `Result`:

```csharp
using ZeroAlloc.Jev;

using var jev = new JevClient(new JevClientOptions { ApiKey = apiKey });

var result = await jev.EvaluateAsync<UrgencyCheck>("Help! My payouts have been failing for 3 days.");

Console.WriteLine(result switch
{
    { IsSuccess: true } => $"P(urgent) = {result.Value.IsUrgent.Probability:P0}",
    _ => $"{result.Error.Kind}: {result.Error.Message}",
});

[JevQuestions]
public partial record UrgencyCheck
{
    [Noul("Does this convey urgency?")]
    public partial Noul IsUrgent { get; }
}
```

`JevClient` answers `EvaluateAsync<T>` over a raw, pooled-buffer UTF-8 path instead of building `SystemOneRequest` and `SystemOneResponse`; any other `IJevClient` (a hand-written fake, for example) falls back to a compatible, allocating default that parses the same typed answers from the untyped call.

For a typed state instead of plain text, name it on the question set with `State = typeof(...)` and call `EvaluateAsync<T, TState>` with a source-generated `JsonTypeInfo<TState>`:

```csharp
using System.Text.Json.Serialization;

var ticket = new TicketContext("Payouts failing", "Help! My payouts have been failing for 3 days.");
var result = await jev.EvaluateAsync<TicketUrgency, TicketContext>(ticket, TicketContextJsonContext.Default.TicketContext);

public sealed record TicketContext(string Subject, string Body);

[JsonSerializable(typeof(TicketContext))]
internal sealed partial class TicketContextJsonContext : JsonSerializerContext;

[JevQuestions(State = typeof(TicketContext))]
public partial record TicketUrgency
{
    [Noul("Does the ticket convey urgency?")]
    public partial Noul IsUrgent { get; }
}
```

Set `JevClientOptions.Model` to send a model or alias other than `JevDefaults.Model` on typed calls.

## Diagnostics

`ZeroAlloc.Jev.Analyzers` ships inside the `ZeroAlloc.Jev` package, next to the `[JevQuestions]` generator, and checks every question set against the Jev API's own rules (JEV001–006) and against what the generator can turn into code (JEV101–107). JEV001 and JEV002 enforce TypeSafe's official SDK schema, which needs at least one option or level. The limits behind JEV005 are only the API sketch's guidance, not a schema limit.

A set with any error, JEV001, JEV002 or JEV101–107, is invalid. The generator emits no `QuestionsUtf8`, no `Parse` and no `IJevQuestionSet` for it. It does implement each unimplemented partial question property with a throwing stub, so a command-line build reports the JEV error instead of CS9248, "partial property must have an implementation part". Only those properties are stubbed. An error in your own declaration, such as CS0238 for `sealed` on a property that overrides nothing, still stops a command-line build before the analyzers run and hides their output. The IDE, which runs the analyzers live, still shows them.

| Id | Severity | Meaning |
|----|----------|---------|
| JEV001 | Error | A `Choice` enum has no members. |
| JEV002 | Error | A `Score` enum has no members. |
| JEV003 | Warning | Instructions or a description is empty or whitespace. |
| JEV004 | Warning | A backticked name in the instructions matches no public member of the `State` type, by lenient matching; against an array `State`, the element type is checked. |
| JEV005 | Warning | A `Score` enum has fewer than 2 or more than 10 levels, or a `Choice` enum has more than 255 options — the API sketch's guidance, not a hard schema limit. |
| JEV006 | Info | A `Choice` member has no `[Criteria]` description. |
| JEV101 | Error | The type carrying `[JevQuestions]` is not a supported shape: a non-generic, non-abstract, non-static, top-level partial class or record that is not file-local. |
| JEV102 | Error | A question property is not a partial, get-only instance property; is `virtual`, `new`, `sealed` or `override`; or is named `Parse` or `QuestionsUtf8`, which the generator reserves. |
| JEV103 | Error | A question property carries more than one question attribute, or one that doesn't match the property type. |
| JEV104 | Error | A `Score` level has no `[Level]` description. |
| JEV105 | Error | The question set has no constructor callable without arguments, or it has `required` members and that constructor lacks `[SetsRequiredMembers]`. A constructor whose parameters all have defaults is accepted. |
| JEV106 | Error | The same wire key is used more than once in the set. |
| JEV107 | Error | The `State` type is not a class, struct, record or array type. |

Two code fixes are offered, and both can fix all occurrences in a document, project or solution:
- **JEV006** adds `[Criteria("…")]` to the `Choice` enum member.
- **JEV104** adds `[Level("…")]` to the `Score` enum member.

Each derives the description from the member's own name: for example, `NeedsAttention` becomes `"Needs attention"`.

Suppress a warning or info diagnostic that does not apply, narrowly, with a reason:

```csharp
#pragma warning disable JEV005 // Score enum intentionally has 12 levels for this question
public enum Satisfaction { /* ... */ }
#pragma warning restore JEV005
```

Or per-project or per-file in `.editorconfig`:

```ini
[*.cs]
dotnet_diagnostic.JEV005.severity = none
```

Suppressing JEV001, JEV002 or JEV101–107 hides the error but does not make the set valid. The generator does not read the diagnostics; it applies the same shared rules itself. So a suppressed set still gets no `QuestionsUtf8`, no `Parse` and no `IJevQuestionSet`, and any use of it fails: `EvaluateAsync<T>` does not compile, and the stubbed properties throw. Fix the declaration instead.

## Testing

`dotnet test` runs the unit tests, the generator tests, the analyzer and code-fix tests, the WireMock integration tests, and the pack tests, which pack the library and check the package's contents. A solution-wide `dotnet test` never makes a billed call: the live smoke tests in `tests/ZeroAlloc.Jev.Live.Tests` call the real APIs and only run when `JEV_LIVE=1` is set *and* the provider's key (`TYPESAFE_API_KEY` or `OPENROUTER_API_KEY`) is set; otherwise every one of them reports skipped. To run them locally, opt in explicitly with the key set: `JEV_LIVE=1 dotnet test tests/ZeroAlloc.Jev.Live.Tests`. Each run makes a few small billed evaluations. Override the model with `JEV_LIVE_MODEL`, or `JEV_LIVE_OPENROUTER_MODEL` for OpenRouter. Maintainers can also run them in CI with the manual **Live smoke** workflow, which reads the keys from the `live-api` environment; restrict that environment's deployment branches to `main`.

## License

[MIT](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/blob/main/LICENSE)
