# Triage report

## Run

Items read: 400 of the dataset's 400. Decided: 400 of 400 (1.0000).

Failed items: none.

Calls by served model: Mapika/decider-2b-GGUF:Q4_K_M 400, jev-1.14.0 104.

## Split

The questions by the route reason, and the tickets whose path had a target call that was not skipped.

### Choice

- Questions: 400
- Confident, kept on the local answer: 299 of 400 (0.7475)
- Escalated, answered by the target: 101 of 400 (0.2525)
- No time left, kept on the local answer: 0 of 400 (0.0000)
- Target failed, kept on the local answer: 0 of 400 (0.0000)
- Tickets with a target call: 101

### Noul

- Questions: 800
- Confident, kept on the local answer: 795 of 800 (0.9938)
- Escalated, answered by the target: 5 of 800 (0.0063)
- No time left, kept on the local answer: 0 of 800 (0.0000)
- Target failed, kept on the local answer: 0 of 800 (0.0000)
- Tickets with a target call: 5 (small: n < 30)

### All

- Questions: 1200
- Confident, kept on the local answer: 1094 of 1200 (0.9117)
- Escalated, answered by the target: 106 of 1200 (0.0883)
- No time left, kept on the local answer: 0 of 1200 (0.0000)
- Target failed, kept on the local answer: 0 of 1200 (0.0000)
- Tickets with a target call: 104 of 400 (0.2600)

## Accuracy

Right answers against the dataset's expected answers.

### Choice

- Local answer alone: 363 of 400 (0.9075)
- Final answer: 369 of 400 (0.9225)
- Target, on the escalated questions: 76 of 101 (0.7525)

### Noul

- Local answer alone: 749 of 800 (0.9363)
- Final answer: 748 of 800 (0.9350)
- Target, on the escalated questions: 3 of 5 (0.6000) (small: n < 30)

### All

- Local answer alone: 1112 of 1200 (0.9267)
- Final answer: 1117 of 1200 (0.9308)
- Target, on the escalated questions: 79 of 106 (0.7453)

## Escalated questions

The questions the target answered, by whether the local and the final answer were right.

### Choice

- Escalated: 101
- Fixed, local wrong and final right: 9 of 101 (0.0891)
- Broken, local right and final wrong: 3 of 101 (0.0297)
- Both right: 67 of 101 (0.6634)
- Both wrong: 22 of 101 (0.2178)

### Noul

- Escalated: 5 (small: n < 30)
- Fixed, local wrong and final right: 1 of 5 (0.2000) (small: n < 30)
- Broken, local right and final wrong: 2 of 5 (0.4000) (small: n < 30)
- Both right: 2 of 5 (0.4000) (small: n < 30)
- Both wrong: 0 of 5 (0.0000) (small: n < 30)

### All

- Escalated: 106
- Fixed, local wrong and final right: 10 of 106 (0.0943)
- Broken, local right and final wrong: 5 of 106 (0.0472)
- Both right: 69 of 106 (0.6509)
- Both wrong: 22 of 106 (0.2075)

## The signal

Whether the below-threshold flag found the local answers that were wrong. Caught means below the threshold, missed means it met the threshold.

### Choice

- Wrong local answers: 37
- Caught: 31 of 37 (0.8378)
- Missed: 6 of 37 (0.1622)
- Local answers below the threshold: 101
- Of those, the local answer was right: 70 of 101 (0.6931)

### Noul

- Wrong local answers: 51
- Caught: 1 of 51 (0.0196)
- Missed: 50 of 51 (0.9804)
- Local answers below the threshold: 5 (small: n < 30)
- Of those, the local answer was right: 4 of 5 (0.8000) (small: n < 30)

### All

- Wrong local answers: 88
- Caught: 32 of 88 (0.3636)
- Missed: 56 of 88 (0.6364)
- Local answers below the threshold: 106
- Of those, the local answer was right: 74 of 106 (0.6981)

## Latency

The time per ticket, and per call. Percentiles are nearest-rank.

- Tickets with a target call: 104, median 5413 ms, p95 5966.3 ms
- Tickets without a target call: 296, median 5282.7 ms, p95 5894.8 ms
- Local calls: 400, median 5282.9 ms, p95 5867.5 ms
- Target calls: 104, median 102.3 ms, p95 180.2 ms

## Cost

Per 1,000 tickets: 0.00597733, in the currency of the prices and reported costs.

Cost came from the provider for 0 tickets, from a price for 400, and was unknown for 0. A ticket with an unknown cost is left out of the total.

Prices used, per million tokens:

- Mapika/decider-2b-GGUF:Q4_K_M: 0 input, 0 output, read 2026-10-17 from runs on your own hardware; no per-token price
- jev-1.14.0: 0.042 input, 0 output, read 2026-11-01 from TypeSafe official pricing page, as supplied by the maintainer on 2026-11-01

## How to read this

Every ticket was answered locally first. The local answer is the baseline: the accuracy of the local answer alone is what the run would have scored without escalation. A question is escalated when its local answer is below the threshold set for it, and the target's answer then replaces the local one.

Fixed counts the escalated questions whose local answer was wrong and whose final answer is right. Broken counts the ones whose local answer was right and whose final answer is wrong. Caught counts the wrong local answers that were below the threshold, missed the wrong local answers that met it.

A question kept on the local answer because no time was left, or because the target failed, counts as the local answer; the target is not credited for it. Cost and latency are measured over the decided tickets, and a ticket with an unknown cost is left out of the cost total.

Every share is printed with the count it is taken from. A count under 30 is marked small: with so few, every figure moves a lot, so treat it as a first look, not a measurement.
