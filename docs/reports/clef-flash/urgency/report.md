# Calibration report: openrouter cloudflare/clef-flash, 2026-10-17

Dataset: banking77-urgency-v1, 60 items.

Responses served by: cloudflare/clef-flash (60)

## Errors

No request failed.

## Summary

| Kind | Scored | Answered | Accuracy | Base rate | ECE | ECE, equal width |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Score | 60 | 60 | 0.8833 | 0.5667 | 0.2190 | 0.2257 |

## Reliability

### Score

| Bin | Answers | Mean confidence | Accuracy |
| ---: | ---: | ---: | ---: |
| 1 | 6 | 0.2645 | 0.5000 |
| 2 | 6 | 0.4243 | 0.5000 |
| 3 | 6 | 0.5162 | 1.0000 |
| 4 | 6 | 0.6125 | 0.8333 |
| 5 | 6 | 0.6988 | 1.0000 |
| 6 | 6 | 0.7464 | 1.0000 |
| 7 | 6 | 0.8020 | 1.0000 |
| 8 | 6 | 0.8296 | 1.0000 |
| 9 | 6 | 0.8575 | 1.0000 |
| 10 | 6 | 0.8914 | 1.0000 |

## Threshold sweep

The full sweep, from 0.00 to 0.99 in steps of 0.01, is in the JSON.

### Score

| Threshold | Coverage | Accuracy | Covered |
| ---: | ---: | ---: | ---: |
| 0.00 | 1.0000 | 0.8833 | 60 |
| 0.30 | 0.9500 | 0.9123 | 57 |
| 0.50 | 0.7667 | 0.9783 | 46 |
| 0.60 | 0.6500 | 1.0000 | 39 |
| 0.70 | 0.5500 | 1.0000 | 33 |
| 0.80 | 0.3667 | 1.0000 | 22 |
| 0.85 | 0.1833 | 1.0000 | 11 |
| 0.90 | 0.0500 | 1.0000 | 3 |
| 0.95 | 0.0000 | n/a | 0 |
| 0.99 | 0.0000 | n/a | 0 |

## Latency

Median 285.2 ms, p95 628.8 ms, over 60 requests.

## Cost

Per 1,000 decisions: 0.0059287, in the currency of the prices and reported costs.

Cost came from the provider for 60 items, from a price for 0, and was unknown for 0. Cached responses left out: 0.

Prices used: none.

## How to read this

Every question with an expected answer is scored against it. The number a threshold gates on is the confidence of the answer: for a Noul question it is the larger of p, the model's probability that the answer is yes, and 1 - p, so it is never below 0.5; for a Choice or Score question it is the confidence the model reports, which can be lower. Use the sweep of each kind, not the one over all kinds, to choose a threshold.

Answers are sorted by confidence and split into up to ten bins of equal size, give or take one answer. In a well-calibrated model the accuracy of each bin is close to its mean confidence. ECE, the expected calibration error, is the gap between the two averaged over the bins, weighted by how many answers each holds: 0 is perfect, and larger is worse. The equal-width column does the same over ten bins of equal confidence width, which can leave bins empty or crowded.

The base rate is what you would score by always giving the most common expected answer for each question. It is worked out per question, over the answered questions only, from the same answers it is compared with, so it is high when a question has few answers: a question answered once always matches its base rate. An accuracy that does not beat it says little.

A failed request is an abstention. Accuracy, base rate and calibration use the answers that came back; the sweep's coverage is a share of all scored questions, so a failed request counts as not covered. An InvalidResponse request was served and billed, so its cost is in the cost and its model in the responses served, but like every failed request it is left out of latency. Responses served from a cache are left out of latency and cost.

With few answers every figure moves a lot: a bin of three answers can only show an accuracy of 0, a third, two thirds or 1. Treat results from a small sample as a first look, not a measurement.
