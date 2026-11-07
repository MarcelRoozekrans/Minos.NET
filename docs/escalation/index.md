---
id: escalation
title: Escalation
sidebar_position: 1
description: Route each question to a cheap decider first and escalate only the unsure ones to a stronger one, with a fallback for failure.
---

# Escalation

Escalation routes each question between deciders. A fast local decider answers first. A stronger decider is asked only
the questions the first one was unsure of. A fallback chain keeps answering when a decider fails. This guide shows how
to size and compose these stages, with the figures of one measured run.

[The pipeline guide](../pipeline.md#escalating-unsure-questions) lists every option of the stage. The pages here are
about using them.

## What routing between deciders is

Three ideas, each with its own stage.

- **A cheap decider first.** Every request goes to a decider that is fast and costs little, such as a model on your own
  hardware.
- **A stronger decider for the unsure questions.** A question whose certainty is below its threshold is asked again of
  a stronger decider, and only that question. Its answer takes the first one's place. This is escalation, and it is
  for doubt: the first decider answered, and was not sure.
- **A fallback for failure.** When a decider does not answer at all, the next client in a chain is asked. This is
  fallback, and it is for failure.

A confident wrong answer is neither doubt nor failure, so neither stage touches it. Only a better threshold or a
stronger first decider catches it.

## The triage result

The triage run sent 400 tickets through a local model, with escalation to a hosted one. Each ticket carried three
questions, so the run answered 1,200 questions. The traffic was replayed from the Banking77 set, not taken from
production. The report gives figures and no verdict, and so does this page.

| Measure | Value |
| --- | --- |
| Tickets | 400 |
| Questions | 1,200 |
| Local answer alone | 0.9267 |
| Final answer | 0.9308 |
| Questions escalated | 106 |
| Target, on the 106 escalated questions | 0.7453 |
| Cost per 1,000 tickets | 0.00597733 |

The two accuracies are taken over the 1,200 questions. The target was right on 79 of the 106 escalated questions. Of
those 106 questions, 10 fixed (a wrong local answer, a right final one) and 5 broken (a right local answer, a wrong
final one). The other 91 kept the same result: 69 right both times and 22 wrong both times. The local answer was confident on 1094 of the 1200 questions, and those kept it.

The cost is the report's figure for 1,000 tickets, in the currency of the prices and reported costs. The report names no
currency. It prices the local model at 0 and the hosted model per million tokens, so the figure counts the hosted calls
only.

The report is [the Banking77 triage report](https://github.com/MarcelRoozekrans/Minos.NET/tree/main/docs/reports/triage/banking77).
Its README records the models, the thresholds and the run.

## When it pays and when it does not

The run showed three things that decide this. They are findings of one run on one dataset, not a rule.

- **The first attempt asked nothing of the target.** With the default budget of 5 seconds, the local call took a median
  of 5.5 s and used the whole budget. All 106 questions below their threshold ended as `NoTimeLeft`, and nothing was
  billed. [The time budget page](time-budget.md) tells the story and how to choose a budget.
- **Noul confidence did not track error.** The local model was wrong on 51 of the 800 Noul questions, and the threshold
  caught 1 of them. For the 400 Choice questions it caught 31 of the 37 wrong answers. A threshold can only escalate the
  questions the certainty marks as unsure. [The thresholds page](thresholds.md) shows how to read this from a report
  of your own.
- **A local call that eats the budget leaves the target no time.** The budget counts the local call first. A slow local
  decider and a small budget give the same result as no escalation, quietly.

Escalation also costs a second call for each ticket with an unsure question (104 of the 400 tickets here), and the escalated answer replaces the first one even when
the target is less sure. Measure both on your own labelled data before you rely on either.

## A map of the recipes

- [Sizing the thresholds](thresholds.md): choose the certainty below which a question is escalated, one threshold per
  question, from a calibration report and the cost of a wrong answer.
- [Setting the time budget](time-budget.md): give escalation a budget that covers the local call and the hosted call,
  and choose it from your own latencies.
- [Falling back to another provider](fallback.md): keep answering when the first decider is down, and read from the path
  which client answered.
- [Composing the stages and keeping data private](composing-and-privacy.md): put escalation outside fallback, check that
  every provider in the chain may see the state, and know what a record keeps.
- [A chat model as the target](chat-model-target.md): use any chat client as the stronger decider, and read where its
  probabilities came from.

## Next

- [The pipeline](../pipeline.md#escalating-unsure-questions): every option of the escalation and fallback stages.
- [The Banking77 triage report](https://github.com/MarcelRoozekrans/Minos.NET/tree/main/docs/reports/triage/banking77): the run behind the figures.
- [Samples](../samples.md): the ticket triage sample that produced the report.
