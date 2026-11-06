# Ticket-triage sample

This sample measures what [escalation](../../docs/pipeline.md) does on labelled traffic. A local model answers every
question first. When its answer is below a threshold set for that question, a hosted model, the target, answers it
instead. The run records both answers, and a second mode turns the records into a report of what was measured: how the
questions split, how accurate the local and the final answers were, what the escalated questions looked like, and the
time and cost per ticket.

The traffic is replayed Banking77 customer messages, not live traffic. Each item asks three questions: an `intent`
Choice question and two Noul questions, `card_not_arrived` and `about_a_payment`. The right answers come from the
dataset's labels.

The sample uses project references, so it runs the code of its own commit.

## What it does

The pipeline is the one an app would build: the `Local` client from the config file, wrapped with `UseEscalation`, and
the `TypeSafe` client as the target. It decides the items one at a time, in dataset order.

| Setting | Value |
| --- | --- |
| Primary name | `local` |
| Target name | `typesafe` |
| Threshold, `intent` | 0.85 |
| Threshold, `card_not_arrived` and `about_a_payment` | 0.80 |
| Expected latency of the target | 300 ms |
| Budget per ticket | 5 s, set with `--budget-seconds` |

The thresholds are taken from the committed local calibration, `docs/reports/local/banking77/report.md`. In that sweep
about 18% of each question kind fell below its threshold.

A question is escalated when the local answer is below its threshold and the budget leaves time for the target. The
route of each question has one of four reasons: confident, escalated, no time left, or target failed. Questions kept on
the local answer for the last two reasons are counted under their own reasons.

Two things to read the numbers with:

- The budget is measured from the escalation stage's entry. It leaves out the time the host spends preparing a request
  before the stage, which a host's own budget would also cover.
- The budget is an option, `--budget-seconds`, because a CPU runner can need more than 5 s for one local call. When the
  local call alone takes longer than the budget, every unsure question is kept on the local answer with the reason no
  time left and the target is never called. The run does not print the budget, so record the value used with the run.
- The local call keeps its retries, as in an app: `MaxRetries` from the `Local` section applies, a retried call spends
  the same budget, and the latency of the local call includes its retries. The records carry no attempt count.

For each item the run writes one line to the records file. It holds the item id, per question the label, the local
answer and certainty, the route and the final answer, per call the client name, the served model, the usage and the
measured latency, and any error kind. It never holds the message text, and neither do the log lines or the report. A
line is logged for each failed item and for every 25 items.

## Run it

The run mode is billed. It needs the local server running and `TYPESAFE_API_KEY` set.

```
dotnet run --project samples/Minos.NET.Samples.TicketTriage -- \
  --dataset datasets/banking77/banking77-minos-v1.jsonl \
  --config docs/reports/minos.json \
  --records triage-records.jsonl
```

The report mode reads the records and calls no provider:

```
dotnet run --project samples/Minos.NET.Samples.TicketTriage -- \
  --report \
  --records triage-records.jsonl \
  --dataset datasets/banking77/banking77-minos-v1.jsonl \
  --prices docs/reports/prices.json \
  --out triage-report
```

| Option | Mode | Meaning |
| --- | --- | --- |
| `--dataset <jsonl>` | both | The labelled dataset, one item per line. |
| `--config <minos.json>` | run | The config file. The `Local` and `TypeSafe` sections under `Minos:Providers` are the two clients. Environment variables such as `Minos__Providers__Local__Model` override the file. |
| `--records <file>` | both | The JSONL records file. The run writes it and the report reads it. |
| `--budget-seconds <n>` | run | Optional. The time one ticket has for the local call and the escalation, as a whole number of seconds from 1 to 300. The default is 5. |
| `--report` | report | Builds the report instead of running. |
| `--prices <prices.json>` | report | The prices of the models, as in `docs/reports/prices.json`. |
| `--out <directory>` | report | Where `triage-report.json` and `triage-report.md` are written. |

The run starts the records file fresh. If `--records` points at an existing file, the sample deletes it before the run,
because the records sink appends and an earlier run's lines would mix into this one.

A run is at most 400 items, the cap of the target's daily budget. A larger dataset exits with code 2 before anything is
sent. The target is called once for each ticket with an unsure question, so a run is billed at most 400 requests.

## Output and exit codes

At the end of a run the sample prints one line:

```
items=<n> decided=<n> failed=<n> escalated_questions=<n> no_time_left=<n> target_failed=<n>
```

The report mode prints the names of the two files it wrote.

| Code | Meaning |
| --- | --- |
| 0 | A run decided at least one item, or the report was written. |
| 1 | A run decided none. |
| 2 | An option is missing or used twice, the budget is not a whole number from 1 to 300, or the dataset, the config, the records or the prices cannot be used, or the dataset has more than 400 items. |
| 130 | Ctrl+C or SIGTERM. The records written so far are kept. |

## The report

`triage-report.md` has these sections, and `triage-report.json` holds the same numbers:

- Run: the items read and decided, the failures by error kind, and the calls by served model.
- Split: the questions by route reason, for each question kind and over all, and the tickets with a target call.
- Accuracy: the local answer alone, the final answer, and the target on the escalated questions.
- Escalated questions: how many the target fixed, how many it broke, and how many were right or wrong both ways.
- The signal: of the wrong local answers, how many were below the threshold and how many met it.
- Latency: the median and the 95th percentile per ticket, with and without a target call, and per local and target call.
- Cost: per 1,000 tickets, in the currency of the prices, with the prices used.

Every share is printed with its count, and a count under 30 is marked small. The report states what was measured. It
does not say whether to adopt escalation; judge that from the numbers.
