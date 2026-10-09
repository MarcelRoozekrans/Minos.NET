---
id: getting-started
title: Getting started
slug: /
sidebar_position: 1
description: Install Minos.NET, point it at TypeSafe or OpenRouter, and run your first typed evaluation.
---

# Getting started

Minos.NET is an unofficial .NET client for [TypeSafe AI](https://typesafe.ai)'s **Jev**, the first System One
model. TypeSafe calls Jev a System One model: it answers typed questions directly instead of generating text, and
[their guide](https://docs.typesafe.ai/concepts/system-one) explains the idea. You send Jev the text or JSON to judge,
called the state, and a few typed questions about it. Jev answers each question with a typed result and a calibrated
confidence, meaning a number meant to track how often such answers are right. You can reach it directly at TypeSafe,
or through OpenRouter.

The library turns those questions and answers into ordinary C# types. You declare the questions on a partial record, a
source generator writes the request, and the reply comes back as properties you can read. There is no JSON to build or
parse by hand.

> **Not affiliated with TypeSafe AI.** Minos.NET is a community project. TypeSafe publishes official SDKs for Python
> and JavaScript; see [docs.typesafe.ai](https://docs.typesafe.ai).

## The three question types

Every question is one of three kinds. The [next page](question-types.md) covers them in detail.

- **Noul** asks a yes/no question and answers with the probability of yes. "Noul" is TypeSafe's name for the
  question type.
- **Choice** asks for one option out of a list, and answers with the pick, a confidence and every option's probability.
- **Score** asks for a level on an ordered scale, and answers like a Choice, plus the probability-weighted average
  level.

## Requirements

You need the .NET 10 SDK, version 10.0.100 or later.

The `[Questions]` source generator compiles against Roslyn 5.0, so your build and your IDE must host Roslyn 5.0 or
later. Visual Studio 2026 version 18.0 is the first release that does. An older IDE shows generator errors even when
`dotnet build` succeeds.

## Install

The packages are not yet published to NuGet. Once they are, `dotnet add package` is how you install them, as below.
Until then you can build them yourself, as the next section shows.

```shell
dotnet add package Minos.NET
```

`Minos.NET` is the core. It already contains the source generator and the [analyzers](diagnostics.md), so there is
nothing else to install for typed questions. If you use dependency injection in a .NET host, add the integration package
as well, which [dependency injection](dependency-injection.md) covers:

```shell
dotnet add package Minos.NET.DependencyInjection
```

### What the package brings with it

`Minos.NET` has one runtime dependency from Microsoft, `Microsoft.Extensions.Logging.Abstractions` 10.0.0 or later,
for the logging interfaces that [the client's logging](observability.md#logging) uses. Its other runtime dependencies
are ZeroAlloc libraries.

NuGet also passes the analyzers and source generators of those dependencies on to your build. The ones that arrive are
those of ZeroAlloc.Validation, ZeroAlloc.Telemetry, and Microsoft.Extensions.Logging.Abstractions. They do nothing
unless you declare `[Validate]` types, `[Instrument]` types or your own `[LoggerMessage]` methods, so your build stays
clean and gets no extra source from them.

### Before the package is published

Clone the [Minos.NET repository](https://github.com/MarcelRoozekrans/Minos.NET), pack both packages into a folder,
and register that folder as a local NuGet source. Use the folder's absolute path. On Windows, give
`dotnet nuget add source` a native path such as `C:\src\minos\nupkgs`, because a path that mixes forward and back
slashes is rejected as invalid.

A local build is versioned as the last release with a `-local` suffix: the release in `.release-please-manifest.json`,
such as `0.4.0`, gives `0.4.0-local`. The packed files' names show it. The suffix keeps a local package apart from
the published one. NuGet caches packages by id and version, so a local build versioned exactly `0.4.0` would sit in the
cache in place of the real 0.4.0, which was built from another commit. A `-local` version is a prerelease, so it sorts
below the release and NuGet never picks it for a stable version range. When the release is itself a prerelease, such
as `1.0.0-beta.1`, a local build is `1.0.0-0.local.beta.1` instead, which sorts below every prerelease of `1.0.0`;
use the version from the packed files' names.

```shell
dotnet pack src/Minos.NET -c Release -o /absolute/path/nupkgs
dotnet pack src/Minos.NET.DependencyInjection -c Release -o /absolute/path/nupkgs
dotnet nuget add source /absolute/path/nupkgs --name minos-net-local
```

Then add the packages with that version, in place of the commands above. Replace `<release>` with the manifest's
version:

```shell
dotnet add package Minos.NET --version <release>-local
dotnet add package Minos.NET.DependencyInjection --version <release>-local
```

Remove the source when you switch to the published packages: `dotnet nuget remove source minos-net-local`.

## Providers and keys

`DecisionClientOptions.Provider` says where requests go, and `DecisionProvider` has two values.

| Provider                 | Requests go to               | Environment variable |
| ------------------------ | ---------------------------- | -------------------- |
| `DecisionProvider.TypeSafe`   | `https://api.typesafe.ai/`   | `TYPESAFE_API_KEY`   |
| `DecisionProvider.OpenRouter` | `https://openrouter.ai/api/` | `OPENROUTER_API_KEY` |

`TypeSafe` is the default. Set `ApiKey` on the options, or leave it unset and the client reads the matching
environment variable. `DecisionClient` is disposable and meant to be long-lived: create one, share it, and dispose it at
shutdown, as the snippet below does for its short example. The client throws `InvalidOperationException` when it is
created with no key available, so a missing key shows up at start-up rather than on the first call.

<!-- snippet: GettingStarted_Clients -->
```cs
// TypeSafe is the default provider. With no ApiKey set, the client reads TYPESAFE_API_KEY.
// DecisionClient is disposable and meant to be long-lived: create it once, share it, and dispose it at shutdown.
public static async Task<string> ViaTypeSafeAsync(string message, CancellationToken cancellationToken)
{
    using var client = new DecisionClient(new DecisionClientOptions());
    return await GettingStartedEvaluation.TriageAsync(client, message, cancellationToken);
}

// OpenRouter: name the provider. With no ApiKey set, the client reads OPENROUTER_API_KEY.
public static async Task<string> ViaOpenRouterAsync(string message, CancellationToken cancellationToken)
{
    using var client = new DecisionClient(new DecisionClientOptions { Provider = DecisionProvider.OpenRouter });
    return await GettingStartedEvaluation.TriageAsync(client, message, cancellationToken);
}
```
<!-- endSnippet -->

## Your first evaluation

Declare the questions as a partial record marked `[Questions]`. This one asks two questions about a support message:
is it urgent, and which team should handle it. The options of the Choice are the members of an enum, each with a short
description that tells Jev what the option means.

<!-- snippet: GettingStarted_Questions -->
```cs
using Minos;

public enum SupportTeam
{
    [Criteria("Payments, invoices and refunds")]
    Billing,

    [Criteria("Bugs, outages and integrations")]
    Technical,

    [Criteria("Pricing, upgrades and new accounts")]
    Sales,
}

// Two questions about one message. The generator writes the question JSON at compile time, and the
// properties hold the typed answers once Jev has replied.
[Questions]
public partial record TicketCheck
{
    [Noul("Does this convey urgency?")]
    public partial Noul IsUrgent { get; }

    [Choice("Which team should handle this?")]
    public partial Choice<SupportTeam> Team { get; }
}
```
<!-- endSnippet -->

Then call `EvaluateAsync<T>` with the text to judge. It returns a `Result`, not the answers directly, because the call
can fail in many ways. Check `IsFailure` first, and read the answers from `Value` only after that.

<!-- snippet: GettingStarted_Evaluate -->
```cs
public static async Task<string> TriageAsync(IDecisionClient client, string message, CancellationToken cancellationToken)
{
    var result = await client.EvaluateAsync<TicketCheck>(message, cancellationToken);

    // Every outcome comes back as a value, so check for failure before reading the answers: a network
    // error, a rejected key and an unreadable response all arrive here, with a Kind and a Message.
    if (result.IsFailure)
    {
        return $"The call failed, {result.Error.Kind}: {result.Error.Message}";
    }

    var check = result.Value;

    // IsUrgent.Probability is the chance that the answer is yes, and Value is that chance at 0.5 or more.
    // Team.Value is the option Jev picked, and Team.Confidence says how far to trust the pick.
    return $"{(check.IsUrgent.Value ? "urgent" : "not urgent")}, for {check.Team.Value}";
}
```
<!-- endSnippet -->

Given a message such as "Help! My payouts have been failing for 3 days.", a typical reply makes this return
`urgent, for Billing`. On failure, `result.Error.Kind` says what went wrong, for example a rejected key or a network
error, and `result.Error.Message` explains it. [The client and its errors](client-and-errors.md#errors) lists every kind
and what to do about each.

The call needs an `IDecisionClient`. `DecisionClient` implements it, and a test can hand in a fake, as
[testing your code](testing-your-code.md) shows. See the [patterns](patterns/index.md) for complete, runnable uses of
these answers.

## Where next

The guide has one page per topic.

- [Question types](question-types.md): Noul, Choice and Score in detail, and how confidence differs from probability.
- [Typed evaluation](typed-evaluation.md): declaring questions, typed state and the evaluate overloads.
- [Question sets at run time](question-sets-at-run-time.md): building a set from data.
- [The client and its errors](client-and-errors.md): options, retries, time-outs and every `DecisionError`.
- [Dependency injection](dependency-injection.md): registering the client in a .NET host.
- [Logging, traces and metrics](observability.md): what the client reports about each call.
- [Native AOT and allocations](native-aot.md): running as a native executable, and the allocation budgets.
- [Diagnostics](diagnostics.md): every analyzer rule for question sets, and how to suppress one.
- [Testing your code](testing-your-code.md): fakes and canned replies, so tests never call the real API.
- [Patterns](patterns/index.md): four ways to put the answers to work.
- [Samples](samples.md): three runnable cookbook samples that replay recorded answers.
- [Performance](performance.md): what a call costs, measured.

## Next

Continue with [Question types](question-types.md).
