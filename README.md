# Minos.NET

![Minos.NET](https://raw.githubusercontent.com/MarcelRoozekrans/Minos.NET/main/assets/icon.png)

[![CI](https://github.com/MarcelRoozekrans/Minos.NET/actions/workflows/ci.yml/badge.svg)](https://github.com/MarcelRoozekrans/Minos.NET/actions/workflows/ci.yml)

Unofficial .NET client for [TypeSafe AI](https://typesafe.ai)'s **Jev**, the first System One model: send a `state` and typed questions (Noul, Choice, Score) and get calibrated, typed answers back — directly from TypeSafe or through OpenRouter.

You declare the questions as a C# type, and a source generator writes the request and the answer parsing at compile time, so generated question sets need no reflection and the client runs under Native AOT.

> **Not affiliated with TypeSafe AI.** Minos.NET is a community project in the [ZeroAlloc](https://github.com/ZeroAlloc-Net) family. TypeSafe publishes official SDKs for Python and JavaScript; see [docs.typesafe.ai](https://docs.typesafe.ai).

**Status:** early development, not yet published to NuGet. See [the roadmap](https://github.com/MarcelRoozekrans/Minos.NET/blob/main/docs/planning/ROADMAP.md).

## Install

```
dotnet add package Minos.NET
dotnet add package Minos.NET.DependencyInjection
```

You need the .NET 10 SDK, and any IDE or build that hosts the `[Questions]` generator must host Roslyn 5.0 or later; [Getting started](https://jev.zeroalloc.net/) has the details.

## Example

<!-- snippet: Readme_Example -->
```cs
using Minos;

public enum Lane
{
    [Criteria("Payments, invoices and refunds")]
    Billing,

    [Criteria("Bugs, outages and integrations")]
    Technical,
}

[Questions]
public partial record Triage
{
    [Noul("Does this convey urgency?")]
    public partial Noul IsUrgent { get; }

    [Choice("Which queue should handle this?")]
    public partial Choice<Lane> Queue { get; }
}

public static class ReadmeExample
{
    // A one-off run. The client reads TYPESAFE_API_KEY. A long-running app creates one client and shares it, or
    // registers it with AddDecisionClient.
    public static async Task<string> RunAsync(string message, CancellationToken cancellationToken)
    {
        using var client = new DecisionClient();
        return await RouteAsync(client, message, cancellationToken);
    }

    public static async Task<string> RouteAsync(IDecisionClient client, string message, CancellationToken cancellationToken)
    {
        var result = await client.EvaluateAsync<Triage>(message, cancellationToken);
        return result.IsFailure
            ? $"{result.Error.Kind}: {result.Error.Message}"
            : $"urgent: {result.Value.IsUrgent.Value}, queue: {result.Value.Queue.Value}";
    }
}
```
<!-- endSnippet -->

## Documentation

The guide lives at [jev.zeroalloc.net](https://jev.zeroalloc.net):

- [Getting started](https://jev.zeroalloc.net/): install Minos.NET, point it at TypeSafe or OpenRouter, and run your first typed evaluation.
- [Question types](https://jev.zeroalloc.net/question-types): Noul, Choice and Score, what each answer holds, and how confidence differs from probability.
- [Typed evaluation](https://jev.zeroalloc.net/typed-evaluation): declare Jev questions as a C# type, give them a typed state, and pick the `EvaluateAsync` overload.
- [Question sets at run time](https://jev.zeroalloc.net/question-sets-at-run-time): build a question set from data with the builder, evaluate it, and read answers through handles.
- [The client and its errors](https://jev.zeroalloc.net/client-and-errors): create and configure a `DecisionClient`, its retries and time-outs, every kind of `DecisionError`, and the raw request API.
- [Dependency injection](https://jev.zeroalloc.net/dependency-injection): register `IDecisionClient` in a .NET host, key several clients, and bind options from configuration.
- [Logging, traces and metrics](https://jev.zeroalloc.net/observability): what the client logs, which spans and metrics it emits, and what it never records.
- [Native AOT and allocations](https://jev.zeroalloc.net/native-aot): what Native AOT compatibility means, the one reflection the client uses, and the allocation budgets that guard it.
- [Diagnostics](https://jev.zeroalloc.net/diagnostics): every JEV analyzer rule with its severity, the two code fixes, and how to suppress a rule.
- [Testing your code](https://jev.zeroalloc.net/testing-your-code): test code that calls Jev with a fake `IDecisionClient` or a real `DecisionClient` over a canned HTTP reply.
- [Patterns](https://jev.zeroalloc.net/patterns): four ways to use Jev answers, each with a guide of its own.
- [Speculative fan-out](https://jev.zeroalloc.net/patterns/fan-out): ask every question you might need in one request and read only the answers that matter.
- [Confidence routing](https://jev.zeroalloc.net/patterns/confidence-routing): gate each action on the answer confidence, with a threshold sized to the cost of being wrong.
- [Composite scoring](https://jev.zeroalloc.net/patterns/composite-scoring): break a judgement into small Scores and combine them with weights you own.
- [Intent routing](https://jev.zeroalloc.net/patterns/intent-routing): make a cheap first decision that sends each request to code, a model or a person.
- [Samples](https://jev.zeroalloc.net/samples): three runnable cookbook samples that replay recorded Jev answers offline.
- [Performance](https://jev.zeroalloc.net/performance): what the benchmarks measure, how to run them, and what the client costs per call.

## Samples

Runnable cookbook apps live in [`samples/`](https://github.com/MarcelRoozekrans/Minos.NET/tree/main/samples); they replay recorded answers, so they run offline.

## Building

Building this repository needs the SDK version pinned in `global.json` (10.0.401 or later, via `rollForward: latestMinor`). The `[Questions]` generator compiles against Roslyn 5.0, so builds and IDEs must host Roslyn 5.0 or later — Visual Studio 2026 version 18.0 is the first release that qualifies. An older IDE shows generator errors even when `dotnet build` succeeds.

## Testing

`dotnet test` runs the unit tests, the generator tests, the analyzer and code-fix tests, the WireMock integration tests, and the pack tests, which pack the library and check the package's contents. A solution-wide `dotnet test` never makes a billed call: the live smoke tests in `tests/Minos.NET.Live.Tests` call the real APIs and only run when `JEV_LIVE=1` is set *and* the provider's key (`TYPESAFE_API_KEY` or `OPENROUTER_API_KEY`) is set; otherwise every one of them reports skipped. To run them locally, opt in explicitly with the key set: `JEV_LIVE=1 dotnet test tests/Minos.NET.Live.Tests`. Each run makes a few small billed evaluations. Override the model with `JEV_LIVE_MODEL`, or `JEV_LIVE_OPENROUTER_MODEL` for OpenRouter. Maintainers can also run them in CI with the manual **Live smoke** workflow, which reads the keys from the `live-api` environment; restrict that environment's deployment branches to `main`.

## License

[MIT](https://github.com/MarcelRoozekrans/Minos.NET/blob/main/LICENSE)
