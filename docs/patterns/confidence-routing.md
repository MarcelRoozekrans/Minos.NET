# Confidence routing

A Jev answer says what it found, and its confidence says whether you should act on it. Treat them as two separate
inputs. The answer picks the action; the confidence decides if the action runs on its own, asks for a check first, or
goes to a person. Each action gets its own gate, sized to what a wrong decision costs: reading a balance can be
wrong and nobody is hurt, while moving money cannot.

TypeSafe describes the pattern in [Confidence routing](https://docs.typesafe.ai/patterns/confidence-routing), and
explains where the numbers come from in their [confidence guide](https://docs.typesafe.ai/confidence). This page shows
it with `ConfidenceThresholds`.

## The tiers

`ConfidenceThresholds.Classify` turns a confidence into one of three `ConfidenceTier` values.

| Tier     | Default range        |
| -------- | -------------------- |
| `Low`    | below 0.5            |
| `Medium` | 0.5 up to below 0.9  |
| `High`   | 0.9 and above        |

The default instance, `ConfidenceThresholds.Default`, uses these two cut points. A `default(ConfidenceThresholds)`
means the same thing, so a struct field you never set already behaves as 0.5 and 0.9. Pass your own pair, as
`new ConfidenceThresholds(medium: 0.6, high: 0.85)`, when an action needs a different gate.

## The questions

One question, with three possible intents.

<!-- snippet: ConfidenceRoutingQuestions -->
```cs
public enum BankingIntent
{
    [Criteria("Asks for an account balance")]
    CheckBalance,

    [Criteria("Asks to move money to another account")]
    TransferMoney,

    [Criteria("Anything else")]
    Other,
}

[JevQuestions]
public partial record BankingRequest
{
    [Choice("What does the caller want to do?")]
    public partial Choice<BankingIntent> Intent { get; }
}
```
<!-- endSnippet -->

## The gates

Balance checks use the defaults, so any answer from Medium up acts. Transfers get a stricter pair, so only a High answer acts, a Medium answer asks the
caller to confirm, and anything lower goes to a person.

<!-- snippet: ConfidenceRoutingRules -->
```cs
public enum BankingAction
{
    CheckBalance,
    ConfirmTransfer,
    Transfer,
    HandToHuman,
}

public static class BankingRouting
{
    // Moving money is expensive to get wrong: act only at 0.85 or above, and ask the caller to confirm from 0.6.
    private static readonly ConfidenceThresholds Transfers = new(medium: 0.6, high: 0.85);

    public static BankingAction Route(BankingRequest request)
    {
        var intent = request.Intent;
        return intent.Value switch
        {
            BankingIntent.CheckBalance => RouteBalanceCheck(intent.Confidence),
            BankingIntent.TransferMoney => RouteTransfer(intent.Confidence),
            _ => BankingAction.HandToHuman,
        };
    }

    // Reading a balance changes nothing, so the documented defaults are enough and Medium is safe to act on.
    private static BankingAction RouteBalanceCheck(double confidence)
        => ConfidenceThresholds.Default.Classify(confidence) == ConfidenceTier.Low
            ? BankingAction.HandToHuman
            : BankingAction.CheckBalance;

    private static BankingAction RouteTransfer(double confidence)
        => Transfers.Classify(confidence) switch
        {
            ConfidenceTier.High => BankingAction.Transfer,
            ConfidenceTier.Medium => BankingAction.ConfirmTransfer,
            _ => BankingAction.HandToHuman,
        };
}
```
<!-- endSnippet -->

## C# notes

- Keep one `static readonly ConfidenceThresholds` per action, next to the code that uses it, so each gate is named and
  reviewed on its own.
- `Classify` is a pair of comparisons on a struct: it allocates nothing.
- A confidence exactly on a threshold goes to the higher tier, so 0.85 is High under `(0.6, 0.85)`. A NaN confidence
  is `Low`.
- Noul answers carry no confidence. Gate them on `Probability` instead, as in the
  [fan-out](fan-out.md) example.
- The thresholds here are starting points. Tune them on your own data.
