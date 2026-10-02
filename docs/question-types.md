---
id: question-types
title: Question types
sidebar_position: 2
description: Noul, Choice and Score, what each answer holds, and how confidence differs from probability.
---

# Question types

Jev answers three kinds of question. This page shows what each answer holds and how to read it. The examples all use
one set of questions about a support message, with a yes/no question, a Choice over departments and a Score for the
customer's mood.

<!-- snippet: QuestionTypes_Questions -->
```cs
public enum Department
{
    [Criteria("Payments, invoices and refunds")]
    Billing,

    [Criteria("Bugs, outages and integrations")]
    Technical,

    [Criteria("Pricing, upgrades and new accounts")]
    Sales,
}

// The members of a Score's enum are its levels, lowest first: the first member is level 0.
public enum Mood
{
    [Level("Annoyed or angry")]
    Annoyed,

    [Level("Neutral")]
    Neutral,

    [Level("Pleased or grateful")]
    Pleased,
}

[JevQuestions]
public partial record TicketAnalysis
{
    [Noul("Does this convey urgency?")]
    public partial Noul IsUrgent { get; }

    [Choice("Which department should handle this?")]
    public partial Choice<Department> Department { get; }

    [Score("How does the customer feel?")]
    public partial Score<Mood> Mood { get; }
}
```
<!-- endSnippet -->

Every answer is a small struct, so reading it never allocates. `IsUrgent` is a `Noul`, `Department` is a
`Choice<Department>` and `Mood` is a `Score<Mood>`.

## Noul: a yes/no probability

A Noul answers a yes/no question with one number: `Probability`, between 0 and 1, that the answer is yes. `Value` is
`true` when that probability is 0.5 or more, for when you only need the call and not the number.

<!-- snippet: QuestionTypes_ReadNoul -->
```cs
public static (bool Yes, double Probability) ReadNoul(Noul urgent)
{
    // Probability is the chance, from 0 to 1, that the answer is yes. A Noul has no confidence:
    // the probability is the whole answer. Value is a convenience that is true from 0.5 up.
    return (urgent.Value, urgent.Probability);
}
```
<!-- endSnippet -->

A Noul has no confidence. The probability is the whole answer: 0.92 means Jev leans strongly towards yes, and 0.5 means
it cannot tell. Compare the probability against a threshold that suits the decision, such as 0.7 to flag something and
0.95 to act on it automatically.

## Choice: one option out of several

A Choice picks one option from a list. The options are the members of an enum, described with `[Criteria]`. The answer
holds three things.

- `Value`: the option Jev picked.
- `Confidence`: how far to trust the pick, between 0 and 1.
- `Probabilities`: the probability of every option, looked up by enum member.

<!-- snippet: QuestionTypes_ReadChoice -->
```cs
public static (Department Picked, double Confidence, double PickedProbability, double SalesProbability) ReadChoice(
    Choice<Department> department)
{
    // Value is the option Jev picked, and Confidence says how far to trust that pick.
    // Probabilities holds every option's probability, looked up by the enum member.
    return (
        department.Value,
        department.Confidence,
        department.Probabilities[department.Value],
        department.Probabilities[Department.Sales]);
}

// The map can be enumerated too, in the order of the enum's members, without allocating.
public static Department? RunnerUp(Choice<Department> department)
{
    Department? runnerUp = null;
    var best = -1.0;
    foreach (var (option, probability) in department.Probabilities)
    {
        if (option != department.Value && probability > best)
        {
            runnerUp = option;
            best = probability;
        }
    }

    return runnerUp;
}
```
<!-- endSnippet -->

Options the response did not mention have probability 0. Reading an enum value that is not one of the question's options
throws `ArgumentOutOfRangeException`, and so does an unknown key or an out-of-range index on a keyed map.

## Score: a level on an ordered scale

A Score is a Choice whose options are ordered, such as a rubric from worst to best. The members of the enum are the
levels, lowest first, and `[Level]` describes each one. The answer holds these.

- `Value`: the most probable level. On a tie, the lower level wins.
- `Expected`: the probability-weighted average level index, such as 1.05. It can sit between levels.
- `Normalized`: `Expected` rescaled to 0 to 1, by dividing by the top level's index.
- `Confidence` and `Probabilities`, as for a Choice.

<!-- snippet: QuestionTypes_ReadScore -->
```cs
public static (Mood Level, double Expected, double Normalized, double Confidence) ReadScore(Score<Mood> mood)
{
    // Value is the most probable level. Expected is the probability-weighted average level index, so it
    // can fall between levels: 0.5 would sit halfway between level 0 and level 1. Normalized rescales it
    // to 0 to 1, so Scores with different numbers of levels can be compared and weighted.
    return (mood.Value, mood.Expected, mood.Normalized, mood.Confidence);
}
```
<!-- endSnippet -->

`Value` is the single most likely level, while `Expected` uses the whole distribution. When the probability is spread
across the levels, `Expected` moves smoothly where `Value` jumps, so it is the better input for a threshold or a
weighted sum. Use `Normalized` when you combine Scores that have different numbers of levels, because 2.0 is the middle
of a five-level scale and the top of a three-level one. The [composite scoring](patterns/composite-scoring.md) pattern
builds on this.

`Normalized` is 0 for an answer with fewer than two levels.

## Keyed questions

When the options or levels are only known at run time, you build the question set with a builder instead of declaring a
type. The Choice and Score then have keyed forms: `KeyedChoice` and `KeyedScore`. They hold the same things, with
strings in place of enum members.

- `KeyedChoice.Value` is the key of the option Jev picked, a string. Its `Probabilities` are a `KeyedProbabilityMap`,
  looked up by key.
- `KeyedScore.Level` is the index of the most probable level, an int, in place of `Value`. Its `Probabilities` are
  keyed by level index, as the strings `"0"`, `"1"` and so on, and an `int` index reads the same entry by position.
  `Expected`, `Normalized` and `Confidence` work as before.

<!-- snippet: QuestionTypes_Keyed -->
```cs
public sealed class PlanAdvisor
{
    private readonly JevQuestionSet _questions;
    private readonly KeyedChoiceHandle _plan;
    private readonly KeyedScoreHandle _effort;

    // The options and levels come from run-time data, so no enum describes them: keys and levels are plain values.
    public PlanAdvisor(IEnumerable<(string Key, string Summary)> plans)
    {
        var built = JevQuestionSet.CreateBuilder()
            .Choice("plan", "Which plan fits this customer?", out _plan, options =>
            {
                foreach (var (key, summary) in plans)
                {
                    options.Option(key, summary);
                }
            })
            .Score("effort", "How much setup work does the customer need?", out _effort, levels => levels
                .Level("Minutes")
                .Level("Hours")
                .Level("Days"))
            .Build();

        _questions = built.IsSuccess ? built.Value : throw new InvalidOperationException(built.Error.Message);
    }

    public async Task<(string Plan, double PlanProbability, int EffortLevel, double EffortNormalized)?> AdviseAsync(
        IJevClient jev, string message, CancellationToken ct)
    {
        var result = await jev.EvaluateAsync(_questions, message, ct);
        if (result.IsFailure)
        {
            return null;
        }

        var plan = result.Value.Get(_plan);       // KeyedChoice: Value is the option's key, a string
        var effort = result.Value.Get(_effort);   // KeyedScore: Level is the level's index, an int

        // Probabilities are looked up by key. For a keyed Score the keys are the level indexes, "0", "1" and so on,
        // and an int index reads the same entry by position.
        return (plan.Value, plan.Probabilities[plan.Value], effort.Level, effort.Normalized);
    }
}
```
<!-- endSnippet -->

Building a set at run time is covered on its own page. The point here is the shape of the answers: they are read with
`JevAnswers.Get` and a handle, and they hold the same numbers as the typed forms.

## Confidence is not probability

The two numbers answer different questions, and a Choice or a Score has both.

- A **probability** says how likely one option, or one level, is. A Choice or Score gives one for every option.
- **Confidence** says how far to trust the answer as a whole. It is meant to be calibrated, so that it can be used as a
  gate; TypeSafe explains what it means in their [confidence guide](https://docs.typesafe.ai/confidence). Treat any
  threshold as a starting point, and tune it on your own data.

So the two do not replace each other. The probabilities show how the options compare, and the confidence helps you
decide what to do with the answer: act on it, check it first, or hand it to a person.

A Noul has no separate confidence. Its probability is the whole answer, so read it against a threshold of your own.

The [confidence routing](patterns/confidence-routing.md) pattern turns this into a rule: gate each action on a
confidence threshold sized to the cost of being wrong, with `ConfidenceThresholds`.

## Reading is allocation-free

The answers of one call share one buffer, and each probability map is a view over it. Reading `Value`, `Probability`,
`Expected`, `Normalized` or `Confidence`, looking up a probability, enumerating a `Probabilities` map and calling
`JevAnswers.Get` all allocate nothing. `foreach` over a map yields each option with its probability, in the order of
the enum's members, as `RunnerUp` above does. A member that repeats an earlier member's value is an alias and is
skipped, and explicit values do not reorder anything. A keyed map enumerates its keys in the order they were added.

The [performance](performance.md) page has the measured costs of a call.

## Next

- [Confidence routing](patterns/confidence-routing.md): act on an answer only as far as its confidence allows.
- [Patterns](patterns/index.md): the four patterns, with runnable examples.
