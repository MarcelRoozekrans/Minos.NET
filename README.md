# Minos

> Named after Minos, the judge of the dead in Greek myth.

![Minos](https://raw.githubusercontent.com/MarcelRoozekrans/Minos.NET/main/assets/brand/icon-192.png)

[![CI](https://github.com/MarcelRoozekrans/Minos.NET/actions/workflows/ci.yml/badge.svg)](https://github.com/MarcelRoozekrans/Minos.NET/actions/workflows/ci.yml)
[![codecov](https://codecov.io/gh/MarcelRoozekrans/Minos.NET/graph/badge.svg)](https://codecov.io/gh/MarcelRoozekrans/Minos.NET)
[![Minos.NET](https://img.shields.io/nuget/v/Minos.NET?label=Minos.NET)](https://www.nuget.org/packages/Minos.NET)
[![Minos.NET.SystemOne](https://img.shields.io/nuget/v/Minos.NET.SystemOne?label=Minos.NET.SystemOne)](https://www.nuget.org/packages/Minos.NET.SystemOne)
[![Minos.NET.OpenAI](https://img.shields.io/nuget/v/Minos.NET.OpenAI?label=Minos.NET.OpenAI)](https://www.nuget.org/packages/Minos.NET.OpenAI)
[![Minos.NET.DependencyInjection](https://img.shields.io/nuget/v/Minos.NET.DependencyInjection?label=Minos.NET.DependencyInjection)](https://www.nuget.org/packages/Minos.NET.DependencyInjection)
[![Minos.NET.Caching](https://img.shields.io/nuget/v/Minos.NET.Caching?label=Minos.NET.Caching)](https://www.nuget.org/packages/Minos.NET.Caching)
[![Minos.NET.RateLimiting](https://img.shields.io/nuget/v/Minos.NET.RateLimiting?label=Minos.NET.RateLimiting)](https://www.nuget.org/packages/Minos.NET.RateLimiting)

Unofficial .NET client for decision models: send a `state` and typed questions (Noul, Choice, Score) and get calibrated, typed answers back.

Minos speaks the `/v1/systemone` protocol of TypeSafe's Jev: to TypeSafe directly, through OpenRouter, or to a server you run, such as a vLLM Semantic Router. The core is provider-neutral, and `Minos.NET.SystemOne` is the client for this protocol. `Minos.NET.OpenAI` is the client for OpenAI's Decisions API, `/v1/decisions`, which is in public beta; its provider name in logs and telemetry is `openai`.

You declare the questions as a C# type, and a source generator writes the request and the answer parsing at compile time, so generated question sets need no reflection and the client runs under Native AOT.

> **Not affiliated with TypeSafe AI.** Minos is a community project. TypeSafe publishes official SDKs for Python and JavaScript; see [docs.typesafe.ai](https://docs.typesafe.ai).

**Status:** version 1.0. The public API is stable and follows semantic versioning. Coming from 0.x? See [Upgrading to 1.0](https://marcelroozekrans.github.io/Minos.NET/pipeline#upgrading-to-10).

## Packages

| Package | What it is for |
| --- | --- |
| [Minos.NET](https://www.nuget.org/packages/Minos.NET) | the provider-neutral core, with typed questions, the source generator, `IDecisionClient` and its pipeline, and the analyzers |
| [Minos.NET.SystemOne](https://www.nuget.org/packages/Minos.NET.SystemOne) | the `/v1/systemone` client for TypeSafe, OpenRouter and servers you run |
| [Minos.NET.OpenAI](https://www.nuget.org/packages/Minos.NET.OpenAI) | the client for OpenAI's Decisions API, not yet tested against the live API |
| [Minos.NET.DependencyInjection](https://www.nuget.org/packages/Minos.NET.DependencyInjection) | registers `IDecisionClient` in a .NET host |
| [Minos.NET.Caching](https://www.nuget.org/packages/Minos.NET.Caching) | a caching stage |
| [Minos.NET.RateLimiting](https://www.nuget.org/packages/Minos.NET.RateLimiting) | a rate-limiting stage |

## What you get

- Typed questions declared as a C# type, with a source generator.
- One provider-neutral `IDecisionClient`, with presets for TypeSafe, OpenRouter and local servers.
- A pipeline with retries, logging, OpenTelemetry traces and metrics, caching and rate limiting.
- Dependency-injection registration, default and keyed.
- The MIN analyzers and their code fixes.
- Native AOT, with allocation budgets enforced in CI.
- A [conformance suite](https://marcelroozekrans.github.io/Minos.NET/conformance), run from this repository, that checks any decision endpoint.

## Install

```
dotnet add package Minos.NET.SystemOne
dotnet add package Minos.NET.DependencyInjection
```

For OpenAI's Decisions API, add `Minos.NET.OpenAI` instead of, or beside, `Minos.NET.SystemOne`. The package is built from OpenAI's Decisions guide and is not yet tested against the live API:

```
dotnet add package Minos.NET.OpenAI
```

Two optional pipeline stages ship as their own packages: `Minos.NET.Caching` answers repeated decisions from an
in-process `IMemoryCache` and an optional `IDistributedCache`, such as Redis, and `Minos.NET.RateLimiting` limits the
call rate with a `RateLimiter`.

You need the .NET 10 SDK, and any IDE or build that hosts the `[Questions]` generator must host Roslyn 5.0 or later; [Getting started](https://marcelroozekrans.github.io/Minos.NET/) has the details.

## Example

<!-- snippet: Readme_Example -->
```cs
using Minos;
using Minos.SystemOne;

public enum Lane
{
    [Criterion("Payments, invoices and refunds")]
    Billing,

    [Criterion("Bugs, outages and integrations")]
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
    // registers it with AddSystemOneDecisionClient.
    public static async Task<string> RunAsync(string message, CancellationToken cancellationToken)
    {
        using var client = new SystemOneDecisionClient();
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

The guide lives at [marcelroozekrans.github.io/Minos.NET](https://marcelroozekrans.github.io/Minos.NET):

- [Getting started](https://marcelroozekrans.github.io/Minos.NET/): install Minos.NET.SystemOne, point it at TypeSafe, OpenRouter or a server of your own, and run your first typed evaluation.
- [Question types](https://marcelroozekrans.github.io/Minos.NET/question-types): Noul, Choice and Score, what each answer holds, and how confidence differs from probability.
- [Typed evaluation](https://marcelroozekrans.github.io/Minos.NET/typed-evaluation): declare questions as a C# type, give them a typed state, and pick the `EvaluateAsync` overload.
- [Question sets at run time](https://marcelroozekrans.github.io/Minos.NET/question-sets-at-run-time): build a question set from data with the builder, evaluate it, and read answers through handles.
- [The client and its errors](https://marcelroozekrans.github.io/Minos.NET/client-and-errors): create and configure a `SystemOneDecisionClient` or an `OpenAIDecisionClient`, retries and time-outs, every kind of `DecisionError`, and the raw request API.
- [Providers](https://marcelroozekrans.github.io/Minos.NET/providers): the presets, servers you run and OpenAI, the capability matrix, images, and switching providers by configuration.
- [Labelled datasets](https://marcelroozekrans.github.io/Minos.NET/datasets): the JSONL format for items with expected answers, the members of a line, how the loader reports problems, and the format version.
- [Dependency injection](https://marcelroozekrans.github.io/Minos.NET/dependency-injection): register `IDecisionClient` in a .NET host, key several clients, and bind options from configuration.
- [The client pipeline](https://marcelroozekrans.github.io/Minos.NET/pipeline): the stages every call goes through, building your own pipeline, writing a stage, and upgrading to 1.0 and from 0.7.
- [Logging, traces and metrics](https://marcelroozekrans.github.io/Minos.NET/observability): what the client logs, which spans and metrics it emits, and what it never records.
- [Native AOT and allocations](https://marcelroozekrans.github.io/Minos.NET/native-aot): what Native AOT compatibility means, the one reflection the client uses, and the allocation budgets that guard it.
- [Diagnostics](https://marcelroozekrans.github.io/Minos.NET/diagnostics): every MIN analyzer rule with its severity, the provider rules the client checks at run time, the two code fixes, and how to suppress a rule.
- [Testing your code](https://marcelroozekrans.github.io/Minos.NET/testing-your-code): test code that calls Jev with a fake `IDecisionClient` or a real `SystemOneDecisionClient` over a canned HTTP reply.
- [Patterns](https://marcelroozekrans.github.io/Minos.NET/patterns): four ways to use Jev answers, each with a guide of its own.
- [Speculative fan-out](https://marcelroozekrans.github.io/Minos.NET/patterns/fan-out): ask every question you might need in one request and read only the answers that matter.
- [Confidence routing](https://marcelroozekrans.github.io/Minos.NET/patterns/confidence-routing): gate each action on the answer confidence, with a threshold sized to the cost of being wrong.
- [Composite scoring](https://marcelroozekrans.github.io/Minos.NET/patterns/composite-scoring): break a judgement into small Scores and combine them with weights you own.
- [Intent routing](https://marcelroozekrans.github.io/Minos.NET/patterns/intent-routing): make a cheap first decision that sends each request to code, a model or a person.
- [Samples](https://marcelroozekrans.github.io/Minos.NET/samples): four runnable cookbook samples that replay recorded Jev answers offline, one of them against three providers chosen by configuration.
- [Performance](https://marcelroozekrans.github.io/Minos.NET/performance): what the benchmarks measure, how to run them, and what the client costs per call.
- [Conformance](https://marcelroozekrans.github.io/Minos.NET/conformance): the suite that checks any decision endpoint's shapes, probabilities, errors and models, and how to run it against your own.

## Samples

Runnable cookbook apps live in [`samples/`](https://github.com/MarcelRoozekrans/Minos.NET/tree/main/samples); all four replay recorded answers, so they run offline, and one of them runs against three providers by configuration.

## Building

Building this repository needs the SDK version pinned in `global.json` (10.0.401 or later, via `rollForward: latestMinor`). The `[Questions]` generator compiles against Roslyn 5.0, so builds and IDEs must host Roslyn 5.0 or later — Visual Studio 2026 version 18.0 is the first release that qualifies. An older IDE shows generator errors even when `dotnet build` succeeds.

Maintainers: [RELEASING.md](https://github.com/MarcelRoozekrans/Minos.NET/blob/main/RELEASING.md) describes how a release is published.

## Testing

`dotnet test` runs the unit tests, the generator tests, the analyzer and code-fix tests, the WireMock integration tests, and the pack tests, which pack the library and check the package's contents. A solution-wide `dotnet test` never makes a billed call: the live smoke tests in `tests/Minos.NET.Live.Tests` call the real APIs and only run when `MINOS_LIVE=1` is set *and* the provider's key (`TYPESAFE_API_KEY` or `OPENROUTER_API_KEY`) is set; otherwise every one of them reports skipped. To run them locally, opt in explicitly with the key set: `MINOS_LIVE=1 dotnet test tests/Minos.NET.Live.Tests`. Each run makes a few small billed evaluations. Override the model with `MINOS_LIVE_MODEL`, or `MINOS_LIVE_OPENROUTER_MODEL` for OpenRouter. The Luna test through OpenRouter also needs `MINOS_LIVE_LUNA=1`, because Luna is unavailable to accounts that allow only zero-data-retention endpoints. There is no live OpenAI test yet. Maintainers can also run them in CI with the manual **Live smoke** workflow, which reads the keys from the `live-api` environment; restrict that environment's deployment branches to `main`.

## About the name

In Greek mythology, **Minos** was king of Crete and, after his death, one of the three judges of the underworld. Every soul
came before him and left with a verdict. That is what this library does. You hand it a state and a few typed questions,
and a decision model returns a verdict for each one: yes or no, which option, how severe, with a calibrated confidence.

Minos does not care which model judges. TypeSafe's Jev, OpenAI's Decisions API or a model running on your own machine
all answer the same typed questions. The name also keeps it in the same Cretan story as its siblings: Minos had
[Daedalus](https://github.com/MarcelRoozekrans/Daedalus.NET) build the Labyrinth, and set
[Talos](https://github.com/MarcelRoozekrans/Thalos.NET) to guard his island.

## License

[MIT](https://github.com/MarcelRoozekrans/Minos.NET/blob/main/LICENSE)
