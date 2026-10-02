---
id: getting-started
title: Getting started
slug: /
sidebar_position: 1
description: Install ZeroAlloc.Jev, point it at TypeSafe or OpenRouter, and run your first typed evaluation.
---

# Getting started

ZeroAlloc.Jev is an unofficial .NET client for [TypeSafe AI](https://typesafe.ai)'s **Jev**, the first System One
model. TypeSafe calls Jev a System One model: it answers typed questions directly instead of generating text, and
[their guide](https://docs.typesafe.ai/concepts/system-one) explains the idea. You send Jev the text or JSON to judge,
called the state, and a few typed questions about it. Jev answers each question with a typed result and a calibrated
confidence, meaning a number meant to track how often such answers are right. You can reach it directly at TypeSafe,
or through OpenRouter.

The library turns those questions and answers into ordinary C# types. You declare the questions on a partial record, a
source generator writes the request, and the reply comes back as properties you can read. There is no JSON to build or
parse by hand.

> **Not affiliated with TypeSafe AI.** ZeroAlloc.Jev is a community project in the
> [ZeroAlloc](https://github.com/ZeroAlloc-Net) family. TypeSafe publishes official SDKs for Python and JavaScript;
> see [docs.typesafe.ai](https://docs.typesafe.ai).

## The three question types

Every question is one of three kinds. The [next page](question-types.md) covers them in detail.

- **Noul** asks a yes/no question and answers with the probability of yes. "Noul" is TypeSafe's name for the
  question type.
- **Choice** asks for one option out of a list, and answers with the pick, a confidence and every option's probability.
- **Score** asks for a level on an ordered scale, and answers like a Choice, plus the probability-weighted average
  level.

## Requirements

You need the .NET 10 SDK, version 10.0.100 or later.

The `[JevQuestions]` source generator compiles against Roslyn 5.0, so your build and your IDE must host Roslyn 5.0 or
later. Visual Studio 2026 version 18.0 is the first release that does. An older IDE shows generator errors even when
`dotnet build` succeeds.

## Install

The packages are not yet published to NuGet. Once they are, `dotnet add package` is how you install them, as below.
Until then you can build them yourself, as the next section shows.

```shell
dotnet add package ZeroAlloc.Jev
```

`ZeroAlloc.Jev` is the core. It already contains the source generator and the analyzers, so there is nothing else to
install for typed questions. If you use dependency injection in a .NET host, add the integration package as well:

```shell
dotnet add package ZeroAlloc.Jev.DependencyInjection
```

### Before the package is published

Clone the [ZeroAlloc.Jev repository](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev), pack both packages into a folder,
and register that folder as a local NuGet source. Use the folder's absolute path. On Windows, give
`dotnet nuget add source` a native path such as `C:\src\jev\nupkgs`, because a path that mixes forward and back
slashes is rejected as invalid. A local build is versioned `0.0.0-local`.

```shell
dotnet pack src/ZeroAlloc.Jev -c Release -o /absolute/path/nupkgs
dotnet pack src/ZeroAlloc.Jev.DependencyInjection -c Release -o /absolute/path/nupkgs
dotnet nuget add source /absolute/path/nupkgs --name zeroalloc-jev-local
```

Then add the packages with that version, in place of the commands above:

```shell
dotnet add package ZeroAlloc.Jev --version 0.0.0-local
dotnet add package ZeroAlloc.Jev.DependencyInjection --version 0.0.0-local
```

Remove the source when you switch to the published packages: `dotnet nuget remove source zeroalloc-jev-local`.

## Providers and keys

`JevClientOptions.Provider` says where requests go, and `JevProvider` has two values.

| Provider                 | Requests go to               | Environment variable |
| ------------------------ | ---------------------------- | -------------------- |
| `JevProvider.TypeSafe`   | `https://api.typesafe.ai/`   | `TYPESAFE_API_KEY`   |
| `JevProvider.OpenRouter` | `https://openrouter.ai/api/` | `OPENROUTER_API_KEY` |

`TypeSafe` is the default. Set `ApiKey` on the options, or leave it unset and the client reads the matching
environment variable. `JevClient` is disposable and meant to be long-lived: create one, share it, and dispose it at
shutdown, as the snippet below does for its short example. The client throws `InvalidOperationException` when it is
created with no key available, so a missing key shows up at start-up rather than on the first call.

<!-- snippet: GettingStarted_Clients -->
```cs
// TypeSafe is the default provider. With no ApiKey set, the client reads TYPESAFE_API_KEY.
// JevClient is disposable and meant to be long-lived: create it once, share it, and dispose it at shutdown.
public static async Task<string> ViaTypeSafeAsync(string message, CancellationToken ct)
{
    using var jev = new JevClient(new JevClientOptions());
    return await GettingStartedEvaluation.TriageAsync(jev, message, ct);
}

// OpenRouter: name the provider. With no ApiKey set, the client reads OPENROUTER_API_KEY.
public static async Task<string> ViaOpenRouterAsync(string message, CancellationToken ct)
{
    using var jev = new JevClient(new JevClientOptions { Provider = JevProvider.OpenRouter });
    return await GettingStartedEvaluation.TriageAsync(jev, message, ct);
}
```
<!-- endSnippet -->

## Your first evaluation

Declare the questions as a partial record marked `[JevQuestions]`. This one asks two questions about a support message:
is it urgent, and which team should handle it. The options of the Choice are the members of an enum, each with a short
description that tells Jev what the option means.

<!-- snippet: GettingStarted_Questions -->
```cs
using ZeroAlloc.Jev;

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
[JevQuestions]
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
public static async Task<string> TriageAsync(IJevClient jev, string message, CancellationToken ct)
{
    var result = await jev.EvaluateAsync<TicketCheck>(message, ct);

    // Every outcome comes back as a value, so check for failure before reading the answers: a network
    // error, a rejected key and an unreadable response all arrive here, with a Kind and a Message.
    if (result.IsFailure)
    {
        return $"Jev failed, {result.Error.Kind}: {result.Error.Message}";
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
error, and `result.Error.Message` explains it.

The call needs an `IJevClient`. `JevClient` implements it, and a test can hand in a fake. See the
[patterns](patterns/index.md) for complete, runnable uses of these answers.

## Where next

The guide has one page per topic.

- [Question types](question-types.md): Noul, Choice and Score in detail, and how confidence differs from probability.
- [Typed evaluation](typed-evaluation.md): declaring questions, typed state and the evaluate overloads.
- [Question sets at run time](question-sets-at-run-time.md): building a set from data.
- [Patterns](patterns/index.md): four ways to put the answers to work.
- [Performance](performance.md): what a call costs, measured.

More pages follow in this guide: the client and its errors, dependency injection, observability, Native AOT,
diagnostics, and testing your code.

## Next

Continue with [Question types](question-types.md).
