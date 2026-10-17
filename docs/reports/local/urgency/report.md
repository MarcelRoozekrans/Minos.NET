# Calibration report: custom Mapika/decider-2b-GGUF:Q4_K_M, 2026-10-17

Dataset: banking77-urgency-v1, 60 items.

Responses served by: Mapika/decider-2b-GGUF:Q4_K_M (60)

## Errors

No request failed.

## Summary

| Kind | Scored | Answered | Accuracy | Base rate | ECE | ECE, equal width |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Score | 60 | 60 | 0.8667 | 0.5667 | 0.3259 | 0.3165 |

## Reliability

### Score

| Bin | Answers | Mean confidence | Accuracy |
| ---: | ---: | ---: | ---: |
| 1 | 6 | 0.1739 | 0.8333 |
| 2 | 6 | 0.3559 | 0.8333 |
| 3 | 6 | 0.4745 | 0.6667 |
| 4 | 6 | 0.5119 | 0.8333 |
| 5 | 6 | 0.5468 | 0.5000 |
| 6 | 6 | 0.5773 | 1.0000 |
| 7 | 6 | 0.6277 | 1.0000 |
| 8 | 6 | 0.6794 | 1.0000 |
| 9 | 6 | 0.7489 | 1.0000 |
| 10 | 6 | 0.8053 | 1.0000 |

## Threshold sweep

The full sweep, from 0.00 to 0.99 in steps of 0.01, is in the JSON.

### Score

| Threshold | Coverage | Accuracy | Covered |
| ---: | ---: | ---: | ---: |
| 0.00 | 1.0000 | 0.8667 | 60 |
| 0.30 | 0.9000 | 0.8704 | 54 |
| 0.50 | 0.7000 | 0.9048 | 42 |
| 0.60 | 0.4000 | 1.0000 | 24 |
| 0.70 | 0.2333 | 1.0000 | 14 |
| 0.80 | 0.0667 | 1.0000 | 4 |
| 0.85 | 0.0000 | n/a | 0 |
| 0.90 | 0.0000 | n/a | 0 |
| 0.95 | 0.0000 | n/a | 0 |
| 0.99 | 0.0000 | n/a | 0 |

## Latency

Median 5043 ms, p95 5565 ms, over 60 requests.

## Cost

Per 1,000 decisions: 0, in the currency of the prices and reported costs.

Cost came from the provider for 0 items, from a price for 60, and was unknown for 0. Cached responses left out: 0.

Prices used, per million tokens:

- Mapika/decider-2b-GGUF:Q4_K_M: 0 input, 0 output, read 2026-10-17 from runs on your own hardware; no per-token price

## How to read this

Every question with an expected answer is scored against it. The number a threshold gates on is the confidence of the answer: for a Noul question it is the larger of p, the model's probability that the answer is yes, and 1 - p, so it is never below 0.5; for a Choice or Score question it is the confidence the model reports, which can be lower. Use the sweep of each kind, not the one over all kinds, to choose a threshold.

Answers are sorted by confidence and split into up to ten bins of equal size, give or take one answer. In a well-calibrated model the accuracy of each bin is close to its mean confidence. ECE, the expected calibration error, is the gap between the two averaged over the bins, weighted by how many answers each holds: 0 is perfect, and larger is worse. The equal-width column does the same over ten bins of equal confidence width, which can leave bins empty or crowded.

The base rate is what you would score by always giving the most common expected answer for each question. It is worked out per question, over the answered questions only, from the same answers it is compared with, so it is high when a question has few answers: a question answered once always matches its base rate. An accuracy that does not beat it says little.

A failed request is an abstention. Accuracy, base rate and calibration use the answers that came back; the sweep's coverage is a share of all scored questions, so a failed request counts as not covered. An InvalidResponse request was served and billed, so its cost is in the cost and its model in the responses served, but like every failed request it is left out of latency. Responses served from a cache are left out of latency and cost.

With few answers every figure moves a lot: a bin of three answers can only show an accuracy of 0, a third, two thirds or 1. Treat results from a small sample as a first look, not a measurement.
