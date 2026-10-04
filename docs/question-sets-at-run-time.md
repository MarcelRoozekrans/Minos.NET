---
id: question-sets-at-run-time
title: Question sets at run time
sidebar_position: 4
description: Build a Jev question set from data with the builder, evaluate it, and read answers through handles.
---

# Question sets at run time

A `[JevQuestions]` type fixes its questions, options and keys when you compile. Sometimes they are not known until the
program runs: the products come from a database, or each tenant has its own teams. For those cases you build the set
with a builder instead. The result is a `JevQuestionSet`, which you evaluate and read much like a typed one.

Prefer a [typed question set](typed-evaluation.md) when the questions are fixed. It needs no handles, and the
[analyzers](diagnostics.md) check it as you type. A built set is checked when you call `Build()`, as
[below](#checking-the-set).

## Building a set

`JevQuestionSet.CreateBuilder()` starts a builder. Each question method adds one question and returns the builder, so
the calls chain, and `Build()` ends the chain.

<!-- snippet: QuestionSets_Build -->
```cs
using ZeroAlloc.Jev;

// The levels of a Score are given to the builder, so the enum needs no attributes.
public enum Priority
{
    Low,
    Medium,
    High,
}

public sealed class TenantRouter
{
    private readonly JevQuestionSet _questions;
    private readonly NoulHandle _urgent;
    private readonly KeyedChoiceHandle _team;
    private readonly ScoreHandle<Priority> _priority;

    // The teams come from a tenant's own data, so no enum can list them. Build the set once, here, and keep it.
    public TenantRouter(IEnumerable<(string Key, string Summary)> teams)
    {
        var built = JevQuestionSet.CreateBuilder()
            .Noul("urgent", "Does this message convey urgency?", out _urgent, criteria => criteria
                .WhenTrue("The sender needs an answer today")
                .WhenFalse("The sender can wait"))
            .Choice("team", "Which team should handle this?", out _team, options =>
            {
                foreach (var (key, summary) in teams)
                {
                    options.Option(key, summary);
                }
            })
            .Score("priority", "How soon should this be handled?", out _priority, levels => levels
                .Level(Priority.Low, "Can wait")
                .Level(Priority.Medium, "This week")
                .Level(Priority.High, "Today"))
            .Build();

        if (built.IsFailure)
        {
            // Failures lists every rule the set breaks, so a bad tenant configuration is reported in full.
            var problems = new List<string>();
            foreach (var failure in built.Error.Failures)
            {
                problems.Add($"{failure.Rule} on '{failure.QuestionKey}': {failure.Message}");
            }

            throw new InvalidOperationException(string.Join("; ", problems));
        }

        _questions = built.Value;
    }

    public async Task<(bool Urgent, string Team, Priority Priority)?> RouteAsync(
        IJevClient jev, string message, CancellationToken ct)
    {
        // The state is a JevContent. Text converts to one, so a string can be passed as it is.
        var result = await jev.EvaluateAsync(_questions, message, ct);
        if (result.IsFailure)
        {
            return null;
        }

        // Each handle gives back the answer type of its question: a Noul, a KeyedChoice and a Score of Priority.
        var answers = result.Value;
        return (answers.Get(_urgent).Value, answers.Get(_team).Value, answers.Get(_priority).Value);
    }
}
```
<!-- endSnippet -->

Every question method takes the same first three arguments.

- **The key**, the question's name in the request and the response. A `null` key throws `ArgumentNullException`. An
  empty or repeated key is caught by `Build()` as JEV106.
- **The instructions**, the question itself, as a [`JevContent`](typed-evaluation.md#jevcontent). A string converts to
  one. Use `JevContent.FromUtf8Json` or `JevContent.FromValue` for instructions that are a JSON object or array.
- **A handle**, as an `out` argument. Keep it: it is how you read this question's answer later.

A fourth argument, the configurator, is a callback that describes the question's options. It is optional for a Noul
and an enum Choice. A keyed Choice, a keyed Score and an enum Score need one: without it, `Build()` fails with JEV001,
JEV002 or JEV104. The methods are these.

| Method | Question | Handle | Configurator |
| --- | --- | --- | --- |
| `Noul` | A yes/no probability. | `NoulHandle` | `WhenTrue` and `WhenFalse` describe what a yes and a no mean. |
| `Choice<TEnum>` | One member of an enum. | `ChoiceHandle<TEnum>` | `Describe` gives a member a description. |
| `Choice` | One option from keys you add. | `KeyedChoiceHandle` | `Option` adds a key, with or without a description. |
| `Score<TEnum>` | A level, each given as an enum member. | `ScoreHandle<TEnum>` | `Level` adds the next level, lowest first. |
| `Score` | A level, each added in order. | `KeyedScoreHandle` | `Level` adds the next level, lowest first. |

The enum forms read the enum once. The keyed forms exist for options that no enum can describe, such as the tenant's
teams above. A keyed Choice answers with a string key, and a keyed Score answers with a level index, as
[Question types](question-types.md#keyed-questions) shows.

### Enum questions

An enum Choice makes every distinct member an option, keyed by its name in snake_case, in declaration order. A member
that repeats an earlier member's value is an alias and is skipped, so that value is keyed by the first name declared
for it. This is how the generator keys them too. Without a configurator, the options are sent with no description, and
`Describe` adds one to a member.

The builder does not read attributes on the members. A `[Criteria]` or `[Level]` on the enum is ignored here. Describe
the options with `Describe`, and give a Score's levels with `Level`. A Score's levels are the members in the order you
add them, lowest first, and each distinct member must be given exactly once. List them in declaration order to match
what the generator would produce for the same enum.

### Describing options

A description is a `JevCriterion`. A string converts to one. For more, build it explicitly.

<!-- snippet: QuestionSets_Criteria -->
```cs
public static (JevQuestionSet Set, ChoiceHandle<ServiceTeam> Team) Create()
{
    var built = JevQuestionSet.CreateBuilder()
        .Choice("team", "Which team should handle this?", out ChoiceHandle<ServiceTeam> team, options => options
            // Text, with examples of what belongs and what does not.
            .Describe(
                ServiceTeam.Billing,
                JevCriterion.Text("Payments, invoices and refunds")
                    .WithExamples("I was charged twice")
                    .WithNotFor("How much is the Pro plan?"))
            // A JSON object or array can be the description as well.
            .Describe(
                ServiceTeam.Technical,
                JevCriterion.Json(JevContent.FromUtf8Json("""{"scope":"bugs","also":["outages","integrations"]}"""u8)))
            // A string converts to a plain text criterion. A member left undescribed is sent with no description.
            .Describe(ServiceTeam.Sales, "Pricing and upgrades"))
        .Build();

    return (built.IsSuccess ? built.Value : throw new InvalidOperationException(built.Error.Message), team);
}
```
<!-- endSnippet -->

`JevCriterion.Text` with `WithExamples` or `WithNotFor` sends the same criterion object that `Examples` and `NotFor`
send on a typed set, and `JevCriterion.Json` sends a JSON object or array. Each `With` method returns a new criterion
and replaces the list set before. A `null` entry in either list is left out. `WithExamples` and `WithNotFor` throw
`InvalidOperationException` on a JSON criterion: put examples inside the JSON instead.

### Configurators work only inside their callback

A configurator is valid while its callback runs. Once the question method returns, calling a stored configurator throws
`InvalidOperationException`, and it cannot change a question that was already added. A question method that throws adds
no question.

## Evaluating and reading

Evaluate a built set with `EvaluateAsync(questionSet, state, ct)`. The state is a `JevContent`, so a string passes as it
is. The result holds a `JevAnswers`, and `answers.Get(handle)` returns the answer for that handle's question, as the
same types a typed set uses: `Noul`, `Choice<T>`, `Score<T>`, `KeyedChoice` and `KeyedScore`. `TenantRouter.RouteAsync`
above shows the whole path. [Question types](question-types.md) covers what those answers hold, and what is free to
read.

A response that is missing the answer to one of the set's questions fails the call with `JevErrorKind.InvalidResponse`.
Answers to keys the set does not contain are ignored.

Any `IJevClient` can evaluate a built set, including a hand-written fake that implements only the two abstract methods.
`JevClient` has a direct path for it, as it does for typed sets.

## Checking the set

`Build()` returns a `Result<JevQuestionSet, JevError>`. It checks the questions against the rules the
[analyzers](diagnostics.md) apply to a typed set, and the rule ids are the same. JEV108 is the builder's own, because
only a built set can carry JSON.

| Rule | What it checks | Outcome |
| --- | --- | --- |
| JEV001 | A Choice has no options: a keyed Choice with none, or an enum Choice over an enum with no members. | Failure |
| JEV002 | A Score has no levels: a keyed Score with none, or an enum Score over an enum with no members. | Failure |
| JEV104 | A member of an enum Score is not given a level. | Failure |
| JEV106 | A question key, a keyed option key or an enum Score member is repeated, or a key is empty. | Failure |
| JEV108 | JSON instructions, a JSON description, or a JSON yes/no meaning nests deeper than 60 levels. | Failure |
| JEV003 | Blank instructions, description or example, or JSON that is exactly `{}` or `[]`. | Warning |
| JEV005 | A Score outside 2 to 10 levels, or a Choice over 255 options. | Warning |

A failure makes `Build()` return an error of kind `JevErrorKind.InvalidQuestions`, and no request is ever sent for the
set. `JevError.Failures` is a read-only list with one `JevQuestionFailure` for each rule broken. It holds the rule's id,
the key of the question at fault, and a message.

<!-- snippet: QuestionSets_Failures -->
```cs
// A set that breaks a rule does not build. Build returns a JevErrorKind.InvalidQuestions error, and its
// Failures list every rule that was broken, each with the key of the question at fault.
public static IReadOnlyList<string> BrokenRules()
{
    var built = JevQuestionSet.CreateBuilder()
        .Choice("team", "Which team should handle this?", out KeyedChoiceHandle _)  // no options
        .Noul("team", "Is this urgent?", out NoulHandle _)                          // a key used twice
        .Build();

    var broken = new List<string>();
    if (built.IsFailure)
    {
        foreach (var failure in built.Error.Failures)
        {
            broken.Add($"{failure.Rule} {failure.QuestionKey}");
        }
    }

    return broken;
}
```
<!-- endSnippet -->

A warning does not stop the build. The set comes back, and `JevQuestionSet.Warnings` lists the advice. It is empty
when there is none.

<!-- snippet: QuestionSets_Warnings -->
```cs
// A warning does not stop the build. The set is returned, with the advice on its Warnings.
public static IReadOnlyList<string> Advice()
{
    var built = JevQuestionSet.CreateBuilder()
        .Noul("urgent", " ", out NoulHandle _)                                       // blank instructions
        .Score("mood", "How does the customer feel?", out KeyedScoreHandle _, levels => levels
            .Level("Neutral"))                                                       // a Score with one level
        .Build();

    var advice = new List<string>();
    if (built.IsSuccess)
    {
        foreach (var warning in built.Value.Warnings)
        {
            advice.Add($"{warning.Rule} {warning.QuestionKey}");
        }
    }

    return advice;
}
```
<!-- endSnippet -->

A `switch` over `JevErrorKind` needs a case for `InvalidQuestions`, since a failed `Build()` is the one place it
appears. [The client and its errors](client-and-errors.md#errors) lists the other kinds.

## Handles

A handle belongs to the builder that made it. It works with the answers to any set that builder built, provided the
question existed when that set was built. `Build()` can be called again after more questions are added, and the handles
from before work with both sets.

`JevAnswers.Get` throws `ArgumentException` for a handle that does not belong to its set. That covers a `default`
handle, a handle from another builder, and a handle for a question added after the set was built. It is a programming
error, so it throws and does not return a failed `Result`.

## Build once and share

Building validates the questions and writes the request JSON, so do it once and keep the set, as `TenantRouter` does in
its constructor. A `JevQuestionSet` is immutable and safe to share across threads. A builder is not thread-safe.

An enum question reads the enum's public fields, which is safe to trim and for [Native
AOT](native-aot.md#the-one-use-of-reflection): the builder's generic parameters are annotated so that the trimmer keeps
them. A method of yours that passes its own generic parameter on to `Choice<T>`, `Score<T>` or `JevAnswers.Get` needs
the same annotation on that parameter: `[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicFields)]`.

## Next

- [The client and its errors](client-and-errors.md): the options, retries, time-outs and every `JevError`.
- [Typed evaluation](typed-evaluation.md): the declared form of a question set, and `JevContent`.
- [Question types](question-types.md): what each answer holds.
- [Fan-out](patterns/fan-out.md#built-at-run-time): five questions built at run time and routed.
