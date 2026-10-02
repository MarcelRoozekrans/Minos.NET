---
id: confidence-routing
title: Confidence routing
sidebar_position: 3
description: Gate each action on the answer confidence, with a threshold sized to the cost of being wrong.
---

# Confidence routing

A Jev answer says what it found, and its confidence says whether you should act on it. Treat them as two separate
inputs. The answer picks the action; the confidence decides if the action runs on its own, asks for a check first, or
goes to a person. Each action gets its own gate, sized to what a wrong decision costs: showing where a parcel is can be
wrong and nobody is hurt, while a refund sent by mistake is money gone.

TypeSafe describes the pattern in [Confidence routing](https://docs.typesafe.ai/patterns/confidence-routing), and
covers confidence itself in their [confidence guide](https://docs.typesafe.ai/confidence). This page shows it with
`ConfidenceThresholds`.

## The tiers

`ConfidenceThresholds.Classify` turns a confidence into one of three `ConfidenceTier` values.

| Tier     | Default range          |
| -------- | ---------------------- |
| `Low`    | under 0.5              |
| `Medium` | from 0.5 to under 0.9  |
| `High`   | 0.9 and above          |

The default cut points, 0.5 and 0.9, are the ones TypeSafe's confidence guide uses in its example. They are a
reasonable start, not a rule. `ConfidenceThresholds.Default` uses them, and so does a `default(ConfidenceThresholds)`,
so a struct field you never set already behaves as 0.5 and 0.9. Pass your own pair, as
`new ConfidenceThresholds(medium: 0.7, high: 0.92)`, when an action needs a different gate.

## The questions

An online shop's support chat asks one question about each message, with four possible intents.

<!-- snippet: ConfidenceRoutingQuestions -->
```cs
public enum ShopIntent
{
    [Criteria("Wants to know where an order is or when it arrives")]
    TrackOrder,

    [Criteria("Wants to cancel an order")]
    CancelOrder,

    [Criteria("Wants money back for an order")]
    RequestRefund,

    [Criteria("Anything else")]
    Other,
}

[JevQuestions]
public partial record ChatMessage
{
    [Choice("What does the customer want to do?")]
    public partial Choice<ShopIntent> Intent { get; }
}
```
<!-- endSnippet -->

## The gates

Each intent has its own gate. Tracking an order uses the defaults, and any answer from Medium up shows the tracking
page. Cancelling uses the defaults too, but only High cancels at once and Medium asks the customer to confirm.
Refunds get a stricter pair: a High answer issues the refund, a Medium answer asks the customer to confirm, and
anything lower goes to a person.

<!-- snippet: ConfidenceRoutingRules -->
```cs
public enum ShopAction
{
    ShowTracking,
    ConfirmCancel,
    CancelOrder,
    ConfirmRefund,
    IssueRefund,
    HandToPerson,
}

public static class ShopRouting
{
    // A refund sends money out and is hard to take back: issue it only from 0.92, and ask the customer to
    // confirm from 0.7.
    private static readonly ConfidenceThresholds Refunds = new(medium: 0.7, high: 0.92);

    public static ShopAction Route(ChatMessage message)
    {
        var intent = message.Intent;
        return intent.Value switch
        {
            ShopIntent.TrackOrder => RouteTracking(intent.Confidence),
            ShopIntent.CancelOrder => RouteCancel(intent.Confidence),
            ShopIntent.RequestRefund => RouteRefund(intent.Confidence),
            _ => ShopAction.HandToPerson,
        };
    }

    // Showing where an order is changes nothing, so the default thresholds are enough and Medium is safe to act on.
    private static ShopAction RouteTracking(double confidence)
        => ConfidenceThresholds.Default.Classify(confidence) == ConfidenceTier.Low
            ? ShopAction.HandToPerson
            : ShopAction.ShowTracking;

    // A cancelled order can be placed again, so cancelling uses the default thresholds, but Medium asks first.
    private static ShopAction RouteCancel(double confidence)
        => ConfidenceThresholds.Default.Classify(confidence) switch
        {
            ConfidenceTier.High => ShopAction.CancelOrder,
            ConfidenceTier.Medium => ShopAction.ConfirmCancel,
            _ => ShopAction.HandToPerson,
        };

    private static ShopAction RouteRefund(double confidence)
        => Refunds.Classify(confidence) switch
        {
            ConfidenceTier.High => ShopAction.IssueRefund,
            ConfidenceTier.Medium => ShopAction.ConfirmRefund,
            _ => ShopAction.HandToPerson,
        };
}
```
<!-- endSnippet -->

## C# notes

- Keep one `static readonly ConfidenceThresholds` per action, next to the code that uses it, so each gate is named and
  reviewed on its own.
- `Classify` is a pair of comparisons on a struct: it allocates nothing.
- A confidence exactly on a threshold goes to the higher tier, so 0.92 is High under `(0.7, 0.92)`. A NaN confidence
  is `Low`.
- Noul (yes/no) answers carry no confidence. Gate them on `Probability` instead, as in the
  [fan-out](fan-out.md) example.
- The thresholds here are starting points. Tune them on your own data.
- The same question can be built at run time instead of declared; the [fan-out](fan-out.md#built-at-run-time) guide
  shows how.
