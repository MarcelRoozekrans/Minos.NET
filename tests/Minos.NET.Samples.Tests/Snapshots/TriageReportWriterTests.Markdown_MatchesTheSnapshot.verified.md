# Triage report

## Run

Items read: 60 of the dataset's 80. Decided: 52 of 60 (0.8667).

Failed items: InvalidResponse 3, Timeout 5.

Calls by served model: jev-1.14.0 17, local-model 52.

## Split

The questions by the route reason, and the tickets whose path had a target call that was not skipped.

### Choice

- Questions: 40
- Confident, kept on the local answer: 30 of 40 (0.7500)
- Escalated, answered by the target: 6 of 40 (0.1500)
- No time left, kept on the local answer: 2 of 40 (0.0500)
- Target failed, kept on the local answer: 2 of 40 (0.0500)
- Tickets with a target call: 8 (small: n < 30)

### Noul

- Questions: 12 (small: n < 30)
- Confident, kept on the local answer: 8 of 12 (0.6667) (small: n < 30)
- Escalated, answered by the target: 2 of 12 (0.1667) (small: n < 30)
- No time left, kept on the local answer: 1 of 12 (0.0833) (small: n < 30)
- Target failed, kept on the local answer: 1 of 12 (0.0833) (small: n < 30)
- Tickets with a target call: 3 (small: n < 30)

### All

- Questions: 52
- Confident, kept on the local answer: 38 of 52 (0.7308)
- Escalated, answered by the target: 8 of 52 (0.1538)
- No time left, kept on the local answer: 3 of 52 (0.0577)
- Target failed, kept on the local answer: 3 of 52 (0.0577)
- Tickets with a target call: 10 of 52 (0.1923)

## Accuracy

Right answers against the dataset's expected answers.

### Choice

- Local answer alone: 28 of 40 (0.7000)
- Final answer: 31 of 40 (0.7750)
- Target, on the escalated questions: 5 of 6 (0.8333) (small: n < 30)

### Noul

- Local answer alone: 9 of 12 (0.7500) (small: n < 30)
- Final answer: 10 of 12 (0.8333) (small: n < 30)
- Target, on the escalated questions: 2 of 2 (1.0000) (small: n < 30)

### All

- Local answer alone: 37 of 52 (0.7115)
- Final answer: 41 of 52 (0.7885)
- Target, on the escalated questions: 7 of 8 (0.8750) (small: n < 30)

## Escalated questions

The questions the target answered, by whether the local and the final answer were right.

### Choice

- Escalated: 6 (small: n < 30)
- Fixed, local wrong and final right: 3 of 6 (0.5000) (small: n < 30)
- Broken, local right and final wrong: 1 of 6 (0.1667) (small: n < 30)
- Both right: 2 of 6 (0.3333) (small: n < 30)
- Both wrong: 0 of 6 (0.0000) (small: n < 30)

### Noul

- Escalated: 2 (small: n < 30)
- Fixed, local wrong and final right: 1 of 2 (0.5000) (small: n < 30)
- Broken, local right and final wrong: 0 of 2 (0.0000) (small: n < 30)
- Both right: 1 of 2 (0.5000) (small: n < 30)
- Both wrong: 0 of 2 (0.0000) (small: n < 30)

### All

- Escalated: 8 (small: n < 30)
- Fixed, local wrong and final right: 4 of 8 (0.5000) (small: n < 30)
- Broken, local right and final wrong: 1 of 8 (0.1250) (small: n < 30)
- Both right: 3 of 8 (0.3750) (small: n < 30)
- Both wrong: 0 of 8 (0.0000) (small: n < 30)

## The signal

Whether the below-threshold flag found the local answers that were wrong. Caught means below the threshold, missed means it met the threshold.

### Choice

- Wrong local answers: 10 (small: n < 30)
- Caught: 7 of 10 (0.7000) (small: n < 30)
- Missed: 3 of 10 (0.3000) (small: n < 30)
- Local answers below the threshold: 10 (small: n < 30)
- Of those, the local answer was right: 2 of 10 (0.2000) (small: n < 30)

### Noul

- Wrong local answers: 3 (small: n < 30)
- Caught: 2 of 3 (0.6667) (small: n < 30)
- Missed: 1 of 3 (0.3333) (small: n < 30)
- Local answers below the threshold: 4 (small: n < 30)
- Of those, the local answer was right: 1 of 4 (0.2500) (small: n < 30)

### All

- Wrong local answers: 13 (small: n < 30)
- Caught: 9 of 13 (0.6923) (small: n < 30)
- Missed: 4 of 13 (0.3077) (small: n < 30)
- Local answers below the threshold: 14 (small: n < 30)
- Of those, the local answer was right: 3 of 14 (0.2143) (small: n < 30)

## Latency

The time per ticket, and per call. Percentiles are nearest-rank.

- Tickets with a target call: 10 (small: n < 30), median 612.5 ms, p95 1204 ms
- Tickets without a target call: 42, median 88 ms, p95 240 ms
- Local calls: 52, median 80 ms, p95 210 ms
- Target calls: 8 (small: n < 30), median 520 ms, p95 1100 ms

## Cost

Per 1,000 tickets: 0.123456, in the currency of the prices and reported costs.

Cost came from the provider for 10 tickets, from a price for 0, and was unknown for 0. A ticket with an unknown cost is left out of the total.

Prices used, per million tokens:

- jev-1.14.0: 0.042 input, 0.084 output, read 2026-11-01 from the provider's price list

## How to read this

Every ticket was answered locally first. The local answer is the baseline: the accuracy of the local answer alone is what the run would have scored without escalation. A question is escalated when its local answer is below the threshold set for it, and the target's answer then replaces the local one.

Fixed counts the escalated questions whose local answer was wrong and whose final answer is right. Broken counts the ones whose local answer was right and whose final answer is wrong. Caught counts the wrong local answers that were below the threshold, missed the wrong local answers that met it.

A question kept on the local answer because no time was left, or because the target failed, counts as the local answer; the target is not credited for it. Cost and latency are measured over the decided tickets, and a ticket with an unknown cost is left out of the cost total.

Every share is printed with the count it is taken from. A count under 30 is marked small: with so few, every figure moves a lot, so treat it as a first look, not a measurement.
