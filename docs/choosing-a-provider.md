---
id: choosing-a-provider
title: Choosing a provider and a threshold
sidebar_position: 7
description: How accurate TypeSafe, Clef Flash and a local model were on a Banking77 sample, how far their confidence can be trusted, what they cost and how fast they answered, and how to pick a confidence threshold.
---

# Choosing a provider and a threshold

Minos sends the same questions to any [provider](providers.md), so the choice of provider is a setting, not a rewrite.
This page helps you make that choice from evidence. For each provider it shows how often the answers were right,
whether the confidence that came with them can be trusted, what 1,000 decisions cost and roughly how fast an answer
came back. Then it shows how to pick the confidence threshold your code acts on.

Every figure here comes from a calibration report committed to the repository, made by `minos calibrate` from
Minos.NET.Tool 1.2.0. A docs test compares each table on this page with those reports, so a figure that drifts from its
report fails the build.

## What was measured

Three providers ran the same two question sets:

- **TypeSafe**, the hosted API, with the model `jev-latest`.
- **Clef Flash**, `cloudflare/clef-flash`, through OpenRouter, with
  [zero data retention](providers.md#privacy-zero-data-retention) switched on.
- **The local model**, `Mapika/decider-2b-GGUF:Q4_K_M`, served on the CI runner itself, as in
  [Running a local server](providers.md#running-a-local-server).

The question sets are a sample of Banking77, a set of customer messages to a bank, each labelled with the customer's
intent. The messages and intent names are by PolyAI, under CC BY 4.0: see the
[license](https://github.com/MarcelRoozekrans/Minos.NET/blob/main/datasets/banking77/LICENSE) and the
[dataset's README](https://github.com/MarcelRoozekrans/Minos.NET/blob/main/datasets/banking77/README.md), which also
says how the sample was built.

- **Banking77** holds 400 messages, 40 from each of 10 card-related intents. Each message asks three questions: a
  Choice, `intent`, that picks one of the 10 intents, and two yes-or-no Nouls, `card_not_arrived` and
  `about_a_payment`. So it gives 400 Choice answers and 800 Noul answers.
- **Urgency** holds 60 of those messages, 6 per intent, with one Score question: how urgent the message is, from 0,
  routine, to 2, money or card security at risk. Level 2 holds only lost or stolen cards. With 60 items its figures
  are indicative only.

The expected answers follow the Banking77 labels, and some labels are debatable: the dataset's README lists the
messages that fit another option better. No provider can score every one of them as expected.

Every request went out one at a time, with `--parallel 1`. Each provider's report folder holds its results, its full
report and a README with the date, the model and the workflow run it came from. When another provider is measured, it
gets its own report folders and a row in each table below.

## The comparison

One table per kind of question, because a Choice, a Noul and a Score report their confidence differently. Each column
means:

- **Items** is the number of messages sent. **Answered** is the number of answers that came back, of those scored.
  **Errors** is the number of requests that failed. Accuracy counts only the answers that came back.
- **Accuracy** is the share of answers that matched the expected answer.
- **ECE**, the expected calibration error, says how far the confidence is from the accuracy. The answers are sorted by
  confidence into equal bins, and ECE is the average gap between each bin's mean confidence and its accuracy. 0 means
  the confidence can be read as the chance of being right; 0.1 means it is off by about ten points. It counts a model
  that is too sure and one that is too modest alike.
- **Bins** is the number of bins and the answers in each: `10 × 40` is ten bins of 40 answers.
- **Coverage at t** is the share of answers whose confidence reached the threshold t, with their count in parentheses.
  **Accuracy at t** is how often those answers were right. [Choosing a threshold](#choosing-a-threshold) says why t is
  0.90 for Choice and Noul and 0.50 for Score.

A Noul answer comes with the probability p that the answer is yes. Its confidence is the larger of p and 1 − p, so a
Noul answer is covered at t when p is at least t or at most 1 − t: a confident no counts as much as a confident yes.

### Banking77 intent (Choice)

| Provider | Model | Items | Answered | Errors | Accuracy | ECE | Bins | Coverage at 0.90 | Accuracy at 0.90 |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| [TypeSafe](https://github.com/MarcelRoozekrans/Minos.NET/tree/main/docs/reports/typesafe/banking77) | `jev-latest`, served as `jev-1.13.0` | 400 | 400 of 400 | 0 | 0.9350 | 0.0312 | 10 × 40 | 0.9200 (368) | 0.9620 |
| [Clef Flash](https://github.com/MarcelRoozekrans/Minos.NET/tree/main/docs/reports/clef-flash/banking77) | `cloudflare/clef-flash` | 400 | 400 of 400 | 0 | 0.9725 | 0.0598 | 10 × 40 | 0.7750 (310) | 1.0000 |
| [The local model](https://github.com/MarcelRoozekrans/Minos.NET/tree/main/docs/reports/local/banking77) | `Mapika/decider-2b-GGUF:Q4_K_M` | 400 | 400 of 400 | 0 | 0.9225 | 0.0199 | 10 × 40 | 0.7500 (300) | 0.9900 |

### Banking77 yes-or-no questions (Noul)

| Provider | Model | Items | Answered | Errors | Accuracy | ECE | Bins | Coverage at 0.90 | Accuracy at 0.90 |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| [TypeSafe](https://github.com/MarcelRoozekrans/Minos.NET/tree/main/docs/reports/typesafe/banking77) | `jev-latest`, served as `jev-1.13.0` | 400 | 800 of 800 | 0 | 0.9363 | 0.0302 | 10 × 80 | 0.8313 (665) | 0.9925 |
| [Clef Flash](https://github.com/MarcelRoozekrans/Minos.NET/tree/main/docs/reports/clef-flash/banking77) | `cloudflare/clef-flash` | 400 | 800 of 800 | 0 | 0.9900 | 0.0421 | 10 × 80 | 0.8462 (677) | 0.9985 |
| [The local model](https://github.com/MarcelRoozekrans/Minos.NET/tree/main/docs/reports/local/banking77) | `Mapika/decider-2b-GGUF:Q4_K_M` | 400 | 800 of 800 | 0 | 0.9387 | 0.0592 | 10 × 80 | 0.6050 (484) | 0.9979 |

### Urgency (Score)

| Provider | Model | Items | Answered | Errors | Accuracy | ECE | Bins | Coverage at 0.50 | Accuracy at 0.50 |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| [TypeSafe](https://github.com/MarcelRoozekrans/Minos.NET/tree/main/docs/reports/typesafe/urgency) | `jev-latest`, served as `jev-1.13.0` | 60 | 60 of 60 | 0 | 0.9667 | 0.0920 | 10 × 6 | 0.9167 (55) | 0.9636 |
| [Clef Flash](https://github.com/MarcelRoozekrans/Minos.NET/tree/main/docs/reports/clef-flash/urgency) | `cloudflare/clef-flash` | 60 | 60 of 60 | 0 | 0.8833 | 0.2190 | 10 × 6 | 0.7667 (46) | 0.9783 |
| [The local model](https://github.com/MarcelRoozekrans/Minos.NET/tree/main/docs/reports/local/urgency) | `Mapika/decider-2b-GGUF:Q4_K_M` | 60 | 60 of 60 | 0 | 0.8667 | 0.3259 | 10 × 6 | 0.7000 (42) | 0.9048 |

### Cost and speed

| Provider | Banking77 cost per 1,000 | Banking77 median latency | Urgency cost per 1,000 | Urgency median latency |
| --- | ---: | ---: | ---: | ---: |
| [TypeSafe](https://github.com/MarcelRoozekrans/Minos.NET/tree/main/docs/reports/typesafe) | 0.0293118 | 57.1 ms | 0.0152376 | 59.7 ms |
| [Clef Flash](https://github.com/MarcelRoozekrans/Minos.NET/tree/main/docs/reports/clef-flash) | 0.0161494 | 221.5 ms | 0.0059287 | 285.2 ms |
| [The local model](https://github.com/MarcelRoozekrans/Minos.NET/tree/main/docs/reports/local) | 0 | 6475 ms | 0 | 5043 ms |

Cost and latency belong to a whole request, and a Banking77 request asks all three of its questions at once. Costs are
in US dollars per 1,000 decisions, where a decision is one request with all its questions.

- **TypeSafe** reports tokens but no cost, so its cost comes from its price: 0.042 dollars per million input tokens
  and 0 per million output tokens, read from TypeSafe's pricing page on 2026-10-17.
- **Clef Flash**'s cost is the cost OpenRouter reported for each request.
- **The local model** shows 0 because it has no per-token price. It costs hardware instead: the machine that runs it,
  and that machine's time.

Latency is the median time from sending a request to reading its answer. It says as much about where the run happened
as about the provider. The hosted providers were called from a GitHub-hosted runner, over the internet, and Clef Flash
goes through OpenRouter on the way. The local model ran on a GitHub-hosted runner with a CPU only, so its median of
about 6.5 seconds reflects that runner, not the model on a GPU. Measure on your own machines before you rely on any
of these times.

## The providers

No run had a failed request, and every provider took every question set: every answer came back, on every set.

Speed is left out of the lists below. The latencies say more about where each run happened than about the provider:
the hosted providers were called from a GitHub-hosted runner over the internet, and the local model ran on a CPU-only
runner. See the note under [Cost and speed](#cost-and-speed).

### TypeSafe

[Its reports](https://github.com/MarcelRoozekrans/Minos.NET/tree/main/docs/reports/typesafe). The answers came from
`jev-1.13.0`, the model `jev-latest` pointed to on the day.

- **Where it is strong:** its confidence clears 0.90 on more of its answers. At 0.90 it keeps 368 of 400 intent
  answers, against 310 and 300 for the others, and 46 of 60 urgency answers, against 3 and 0. The urgency counts are
  small, and the urgency table itself is read at 0.50. On urgency it was right on 58 of 60 messages, though 60
  messages are too few to rank it above the others.
- **Where it falls down:** its Choice confidence crowds near 1. Most of its intent answers carry a confidence of 0.99
  or more, and some of them are wrong, so raising the threshold past 0.90 removes few wrong answers: see the intent
  sweep below. Its least confident yes-or-no answers were right less often than their confidence said.

### Clef Flash through OpenRouter

[Its reports](https://github.com/MarcelRoozekrans/Minos.NET/tree/main/docs/reports/clef-flash). Every request asked
OpenRouter for zero data retention.

- **Where it is strong:** its yes-or-no accuracy is clear of both others by more than the uncertainty, and its intent
  accuracy is clear of the local model's: see [Reading the uncertainty](#reading-the-uncertainty). On intent its
  accuracy is also the highest point estimate, but its interval overlaps TypeSafe's. Its intent answers at 0.90 or
  above were right every time.
- **Where it falls down:** its confidence is too modest. In every bin of its three reports the answers were right more
  often than the confidence said, so its ECE comes from being too modest, not too sure. A modest confidence costs
  coverage, not correctness: at 0.90 it keeps fewer intent answers than TypeSafe does, and on urgency its confidence
  almost never reaches 0.90.

### The local model

[Its reports](https://github.com/MarcelRoozekrans/Minos.NET/tree/main/docs/reports/local). It runs on your own
hardware, so no message leaves your network.

- **Where it is strong:** its intent confidence was the closest to its accuracy, with the smallest ECE in the intent
  table on this sample, so a threshold on its intent answers means close to what it says. It has no per-token price.
- **Where it falls down:** its Score confidence is far too modest: it never reaches 0.90 on urgency, and its ECE there
  is the largest on the page. Its yes-or-no confidence is modest too, so at 0.90 it keeps the fewest Noul answers.

## Choosing a threshold

A threshold trades coverage for accuracy. The higher it is, the fewer answers your code acts on alone, and the more
often those are right. The rest go to a person, a fallback or a second question. The sweeps below show the trade on
each provider: each cell is the coverage, then the accuracy of the covered answers, at the threshold in the column
heading.

### Banking77 intent sweep

| Provider | 0.50 | 0.70 | 0.90 | 0.95 | 0.99 |
| --- | ---: | ---: | ---: | ---: | ---: |
| [TypeSafe](https://github.com/MarcelRoozekrans/Minos.NET/tree/main/docs/reports/typesafe/banking77) | 0.9825 / 0.9364 | 0.9525 / 0.9449 | 0.9200 / 0.9620 | 0.8900 / 0.9635 | 0.8525 / 0.9677 |
| [Clef Flash](https://github.com/MarcelRoozekrans/Minos.NET/tree/main/docs/reports/clef-flash/banking77) | 0.9700 / 0.9820 | 0.9500 / 0.9895 | 0.7750 / 1.0000 | 0.5300 / 1.0000 | 0.0300 / 1.0000 |
| [The local model](https://github.com/MarcelRoozekrans/Minos.NET/tree/main/docs/reports/local/banking77) | 0.9575 / 0.9452 | 0.9100 / 0.9643 | 0.7500 / 0.9900 | 0.6225 / 0.9920 | 0.2525 / 1.0000 |

### Banking77 yes-or-no questions sweep

| Provider | 0.50 | 0.70 | 0.90 | 0.95 | 0.99 |
| --- | ---: | ---: | ---: | ---: | ---: |
| [TypeSafe](https://github.com/MarcelRoozekrans/Minos.NET/tree/main/docs/reports/typesafe/banking77) | 1.0000 / 0.9363 | 0.9313 / 0.9651 | 0.8313 / 0.9925 | 0.7725 / 0.9968 | 0.1263 / 0.9901 |
| [Clef Flash](https://github.com/MarcelRoozekrans/Minos.NET/tree/main/docs/reports/clef-flash/banking77) | 1.0000 / 0.9900 | 0.9875 / 0.9962 | 0.8462 / 0.9985 | 0.6637 / 1.0000 | 0.0600 / 1.0000 |
| [The local model](https://github.com/MarcelRoozekrans/Minos.NET/tree/main/docs/reports/local/banking77) | 1.0000 / 0.9387 | 0.9137 / 0.9713 | 0.6050 / 0.9979 | 0.2838 / 1.0000 | 0.0000 / n/a |

### Urgency sweep

| Provider | 0.30 | 0.50 | 0.70 | 0.90 |
| --- | ---: | ---: | ---: | ---: |
| [TypeSafe](https://github.com/MarcelRoozekrans/Minos.NET/tree/main/docs/reports/typesafe/urgency) | 0.9833 / 0.9661 | 0.9167 / 0.9636 | 0.8500 / 0.9804 | 0.7667 / 1.0000 |
| [Clef Flash](https://github.com/MarcelRoozekrans/Minos.NET/tree/main/docs/reports/clef-flash/urgency) | 0.9500 / 0.9123 | 0.7667 / 0.9783 | 0.5500 / 1.0000 | 0.0500 / 1.0000 |
| [The local model](https://github.com/MarcelRoozekrans/Minos.NET/tree/main/docs/reports/local/urgency) | 0.9000 / 0.8704 | 0.7000 / 0.9048 | 0.2333 / 1.0000 | 0.0000 / n/a |

The tables above are read at 0.90 for Choice and Noul. That is the high threshold of `ConfidenceThresholds.Default`,
the point where Minos's defaults let an answer act alone, and on Banking77 it keeps most answers on every provider
while their accuracy rises. For Score the reference is 0.50, the default's medium threshold. Score confidences ran
lower: at 0.90 Clef Flash kept 3 of the 60 urgency answers and the local model none, so a 0.90 column would hide what
the providers can do.

The same threshold does not mean the same thing on every provider. A modest provider keeps fewer answers at a given
threshold, and a provider whose confidence crowds near 1 keeps almost all of them, wrong ones included. So pick the
threshold after you pick the provider, and for each kind of question:

1. Decide what a wrong action costs, and how many answers you can send to a person.
2. In the provider's sweep for that kind, find the lowest threshold whose accuracy you can live with.
3. Check its coverage. If too few answers reach it, the provider may be too modest for that question; a lower
   threshold or another provider may fit better.
4. Put the threshold in a `ConfidenceThresholds`, and measure again on your own data.

`ConfidenceThresholds.Classify` places a confidence in a tier: `High` from the high threshold, `Medium` from the
medium one, and `Low` below it. Keep one pair per action. Here the topic gate uses 0.90, and the urgency gate uses the
lower 0.50 that Score answers called for:

<!-- snippet: ChoosingAProvider_Gates -->
```cs
public enum SupportTopic
{
    [Criterion("The card has not arrived, or the customer asks when it will")]
    CardArrival,

    [Criterion("The card is lost or stolen")]
    LostOrStolenCard,

    [Criterion("A card payment was declined")]
    DeclinedCardPayment,
}

public enum UrgencyLevel
{
    [Level("Routine: a question, with nothing at risk")]
    Routine,

    [Level("Needs action soon: the customer is blocked")]
    Soon,

    [Level("Urgent: money or card security at risk")]
    Urgent,
}

[Questions]
public partial record SupportMessage
{
    [Choice("Which topic is this customer message about?")]
    public partial Choice<SupportTopic> Topic { get; }

    [Score("How urgent is this message?")]
    public partial Score<UrgencyLevel> Urgency { get; }
}

public enum SupportRoute
{
    TopicQueue,
    ConfirmTopic,
    Person,
    OnCall,
}

public static class SupportGates
{
    // Choice answers: act from 0.9, the reference threshold of the Banking77 intent table, and let a person decide
    // below 0.5.
    private static readonly ConfidenceThresholds TopicGate = new(medium: 0.5, high: 0.9);

    // Score answers ran less confident than Choice answers on the urgency set, so their gate is lower: 0.5, the
    // reference threshold of the urgency table. These are starting points; sweep your own data before you copy them.
    private static readonly ConfidenceThresholds UrgencyGate = new(medium: 0.3, high: 0.5);

    public static SupportRoute Route(SupportMessage message)
    {
        if (message.Urgency.Value == UrgencyLevel.Urgent)
        {
            // A lost card that waits costs more than a needless call, so even a Medium urgent answer reaches a person.
            var urgency = UrgencyGate.Classify(message.Urgency.Confidence);
            if (urgency == ConfidenceTier.High)
            {
                return SupportRoute.OnCall;
            }

            if (urgency == ConfidenceTier.Medium)
            {
                return SupportRoute.Person;
            }
        }

        return TopicGate.Classify(message.Topic.Confidence) switch
        {
            ConfidenceTier.High => SupportRoute.TopicQueue,
            ConfidenceTier.Medium => SupportRoute.ConfirmTopic,
            _ => SupportRoute.Person,
        };
    }
}
```
<!-- endSnippet -->

[Confidence routing](patterns/confidence-routing.md) shows the tiers in a fuller example.

## Measuring on your own data

The Banking77 sample tells you how the providers behave on short banking messages. Your own messages are the better
test. Write a few hundred of them as a [labelled dataset](datasets.md), with the answer you expect to each question,
and run it through each provider you are considering:

```
minos calibrate --dataset my-messages.jsonl --provider TypeSafe --parallel 1
```

The report has the same accuracy, ECE, bins and sweep as the tables on this page, per kind of question. A provider
that does not report its cost needs a prices file; [the command line](datasets.md#running-it-from-the-command-line)
describes it and the other options. Run each provider from the place it will run in production, so the latency means
something.

## Reading the uncertainty

A sample of 400 messages, and 60 for urgency, gives each accuracy a margin of error. The table gives each one's right
answers and its 95% Wilson interval: the range of accuracies that could well have produced that count. The interval is
narrower for 800 Noul answers than for 60 Score answers.

### Intervals

| Provider | Banking77 intent | Banking77 yes-or-no | Urgency |
| --- | ---: | ---: | ---: |
| [TypeSafe](https://github.com/MarcelRoozekrans/Minos.NET/tree/main/docs/reports/typesafe) | 374 of 400, 0.9065 to 0.9553 | 749 of 800, 0.9171 to 0.9512 | 58 of 60, 0.8864 to 0.9908 |
| [Clef Flash](https://github.com/MarcelRoozekrans/Minos.NET/tree/main/docs/reports/clef-flash) | 389 of 400, 0.9514 to 0.9846 | 792 of 800, 0.9804 to 0.9949 | 53 of 60, 0.7782 to 0.9423 |
| [The local model](https://github.com/MarcelRoozekrans/Minos.NET/tree/main/docs/reports/local) | 369 of 400, 0.8921 to 0.9449 | 751 of 800, 0.9199 to 0.9534 | 52 of 60, 0.7583 to 0.9309 |

This page treats a difference between two providers as shown only when their intervals do not overlap. That is a
cautious rule: overlapping intervals do not prove two providers equal, they mean this sample cannot tell them apart.

- **Banking77 intent:** Clef Flash's interval lies above the local model's. TypeSafe's overlaps both, so it is not
  ranked against either.
- **Banking77 yes-or-no:** Clef Flash's interval lies above both others. TypeSafe's and the local model's overlap.
- **Urgency:** all three overlap. 60 messages cannot rank these providers on urgency.

Two more cautions apply. Every provider answered the same messages, and some of their labels are debatable, so a gap
of a few answers can come from a handful of messages. And a run is one day of one model version: a hosted model can
change behind the same name, as `jev-latest` does. Measure again when you change provider or model.

## Next

- [Labelled datasets](datasets.md): the dataset format, the report, and the `minos` command line.
- [Providers](providers.md): how to configure each provider measured here.
- [Confidence routing](patterns/confidence-routing.md): gate each action on the confidence of its answer.
- [Question types](question-types.md#confidence-is-not-probability): why confidence and probability differ.
