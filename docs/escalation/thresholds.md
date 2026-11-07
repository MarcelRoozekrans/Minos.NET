---
id: thresholds
title: Sizing the thresholds
sidebar_position: 2
description: Choose the certainty below which a question is escalated, one threshold per question, from a calibration report and the cost of a wrong answer.
---

# Sizing the thresholds

A threshold is the certainty below which a question goes to the stronger decider. Set it too low and wrong answers
stay with the cheap decider. Set it too high and most questions escalate, and the cheap decider saves nothing. This page
shows how to size one threshold for each question.

[The pipeline guide](../pipeline.md#escalating-unsure-questions) lists every option of the stage. This page is about the
numbers.

## How certainty is read

Escalation compares a certainty with a threshold. The certainty of a Choice or a Score is its `Confidence`. The
certainty of a Noul is the larger of `p` and `1 − p`, so a Noul answer of 0.05 is as certain as one of 0.95, and a Noul
certainty is never below 0.5.

A question is escalated when its certainty is below its threshold. `DefaultThreshold` is 0.6, and it applies to every
question that has no threshold of its own.

## Size a threshold to the cost of being wrong

A wrong answer costs more in some questions than in others. Pick each threshold from what a wrong answer costs, and from
what an escalation costs.

- A question whose wrong answer is cheap can keep the default, or a lower threshold.
- A question whose wrong answer is costly gets a higher threshold, so fewer wrong answers stay with the cheap decider.
  A higher threshold escalates more questions.

If your app already acts only above a floor of its own, keep the escalation threshold at or above that floor. Then
an answer that stays with the cheap decider is already above the app's floor, and the two gates do not both stop the
same answers. With the threshold below the floor, an answer between the two would be kept but not acted on.
[Confidence routing](../patterns/confidence-routing.md#the-gates) shows the floor of each action.

## One threshold per question key

`Thresholds` holds a threshold for each question key, over `DefaultThreshold`. A key the question set does not have is
ignored, so one set of options can serve several question sets.

<!-- snippet: Escalation_Thresholds -->
```cs
// A question the local model is less than 0.6 sure of is asked again of "hosted", unless it has a threshold of its
// own. The keys are the question keys of the Banking77 set, and a key the set does not have is ignored.
public static IServiceCollection AddTriage(IServiceCollection services, string localKey, string hostedKey)
{
    services.AddSystemOneDecisionClient("hosted", options => options.ApiKey = hostedKey);
    services.AddSystemOneDecisionClient(options => options.ApiKey = localKey)
        .UseEscalation("hosted", options =>
        {
            options.PrimaryName = "local";
            options.DefaultThreshold = 0.6;
            options.Thresholds["intent"] = 0.85;
            options.Thresholds["card_not_arrived"] = 0.80;
            options.Thresholds["about_a_payment"] = 0.80;
        });
    return services;
}
```
<!-- endSnippet -->

The triage run described below used these three values: 0.85 for the Choice question `intent`, and 0.80 for the two Noul
questions.

## Read a threshold off the calibration report

A calibration report shows what each threshold would cover. Run the cheap decider over a labelled set with `minos
calibrate`, as [Labelled datasets](../datasets.md) describes, and read the threshold sweep of the kind you are sizing.
Use the sweep of each kind, not the one over all kinds.

Here is the row at 0.85 in the sweep of the Choice question, from the committed report of the local model on
Banking77:

| Threshold | Coverage | Accuracy | Covered |
| ---: | ---: | ---: | ---: |
| 0.85 | 0.8200 | 0.9787 | 328 |

The columns mean:

- **Threshold** is the certainty an answer must reach.
- **Covered** is the number of answers that reached it: 328 of the 400 Choice questions.
- **Coverage** is that number as a share of all the questions scored: 328 of 400 is 0.8200.
- **Accuracy** is the share of the covered answers that were right: 0.9787.

At 0.85 the local model keeps 328 of the 400 Choice questions and gets 0.9787 of those right. The other 72 questions
are the ones escalation would ask again, about. The triage run below was a separate run of the local model. It
escalated 101 of the 400 Choice questions at 0.85, not 72, so read the sweep as an estimate. Move the threshold up and the accuracy of what stays rises, while coverage falls,
and with it the share of questions the cheap decider answers.

## What confidence caught in the triage run

The triage run sent the 400 Banking77 tickets through the local model, with escalation to a hosted one. Each ticket had
three questions: one Choice and two Noul, so the run decided 400 Choice questions and 800 Noul questions. The run used the
three per-key thresholds in the snippet above.

The local model alone got 37 of the 400 Choice questions wrong and 51 of the 800 Noul questions wrong. The threshold
caught the Choice errors much better than the Noul ones:

| Question kind | Wrong answers | Below the threshold | Slipped through |
| --- | ---: | ---: | ---: |
| Choice | 37 | 31 | 6 |
| Noul | 51 | 1 | 50 |

So 31 of the 37 wrong Choice answers were escalated, and 6 slipped through. Only 1 of the 51 wrong Noul answers
was, and 50 slipped through. A wrong Noul answer is mostly a sure one. A Noul threshold of 0.80
escalated 5 of the 800 Noul questions, so it helps little.

A threshold only helps where confidence tracks error. Check it on the sweep before you pay for the escalation: if
the accuracy of the covered answers hardly rises as the threshold rises, a higher threshold only escalates more.

## Next

- [The pipeline](../pipeline.md#escalating-unsure-questions): every option of the escalation stage.
- [Confidence routing](../patterns/confidence-routing.md): gates for the actions, sized to the cost of being wrong.
- [Choosing a provider and a threshold](../choosing-a-provider.md): how the calibration report is read.
- [Labelled datasets](../datasets.md): run `minos calibrate` over your own set.
- [The Banking77 calibration report](https://github.com/MarcelRoozekrans/Minos.NET/tree/main/docs/reports/local/banking77): the report this page reads.
- [The Banking77 triage report](https://github.com/MarcelRoozekrans/Minos.NET/tree/main/docs/reports/triage/banking77): the run behind the triage figures.
