# Calibration report: typesafe jev-latest, 2026-10-17

Dataset: banking77-minos-v1, 400 items.

Responses served by: jev-1.13.0 (400)

## Errors

No request failed.

## Summary

| Kind | Scored | Answered | Accuracy | Base rate | ECE | ECE, equal width |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Noul | 800 | 800 | 0.9363 | 0.7500 | 0.0302 | 0.0322 |
| Choice | 400 | 400 | 0.9350 | 0.1000 | 0.0312 | 0.0551 |
| All kinds | 1200 | 1200 | 0.9358 | 0.5333 | 0.0244 | 0.0168 |

## Reliability

### Noul

| Bin | Answers | Mean confidence | Accuracy |
| ---: | ---: | ---: | ---: |
| 1 | 80 | 0.6425 | 0.5375 |
| 2 | 80 | 0.8679 | 0.8500 |
| 3 | 80 | 0.9500 | 0.9875 |
| 4 | 80 | 0.9664 | 1.0000 |
| 5 | 80 | 0.9715 | 1.0000 |
| 6 | 80 | 0.9800 | 1.0000 |
| 7 | 80 | 0.9800 | 1.0000 |
| 8 | 80 | 0.9800 | 1.0000 |
| 9 | 80 | 0.9826 | 1.0000 |
| 10 | 80 | 0.9900 | 0.9875 |

### Choice

| Bin | Answers | Mean confidence | Accuracy |
| ---: | ---: | ---: | ---: |
| 1 | 40 | 0.6845 | 0.6750 |
| 2 | 40 | 0.9788 | 0.8750 |
| 3 | 40 | 0.9988 | 0.9000 |
| 4 | 40 | 1.0000 | 0.9500 |
| 5 | 40 | 1.0000 | 0.9750 |
| 6 | 40 | 1.0000 | 1.0000 |
| 7 | 40 | 1.0000 | 0.9750 |
| 8 | 40 | 1.0000 | 1.0000 |
| 9 | 40 | 1.0000 | 1.0000 |
| 10 | 40 | 1.0000 | 1.0000 |

### All kinds

| Bin | Answers | Mean confidence | Accuracy |
| ---: | ---: | ---: | ---: |
| 1 | 120 | 0.6452 | 0.5833 |
| 2 | 120 | 0.9038 | 0.8917 |
| 3 | 120 | 0.9625 | 0.9917 |
| 4 | 120 | 0.9734 | 0.9917 |
| 5 | 120 | 0.9800 | 1.0000 |
| 6 | 120 | 0.9800 | 1.0000 |
| 7 | 120 | 0.9868 | 0.9667 |
| 8 | 120 | 0.9963 | 0.9500 |
| 9 | 120 | 1.0000 | 0.9833 |
| 10 | 120 | 1.0000 | 1.0000 |

## Threshold sweep

The full sweep, from 0.00 to 0.99 in steps of 0.01, is in the JSON.

### Noul

| Threshold | Coverage | Accuracy | Covered |
| ---: | ---: | ---: | ---: |
| 0.00 | 1.0000 | 0.9363 | 800 |
| 0.30 | 1.0000 | 0.9363 | 800 |
| 0.50 | 1.0000 | 0.9363 | 800 |
| 0.60 | 0.9688 | 0.9548 | 775 |
| 0.70 | 0.9313 | 0.9651 | 745 |
| 0.80 | 0.8925 | 0.9818 | 714 |
| 0.85 | 0.8700 | 0.9871 | 696 |
| 0.90 | 0.8313 | 0.9925 | 665 |
| 0.95 | 0.7725 | 0.9968 | 618 |
| 0.99 | 0.1263 | 0.9901 | 101 |

### Choice

| Threshold | Coverage | Accuracy | Covered |
| ---: | ---: | ---: | ---: |
| 0.00 | 1.0000 | 0.9350 | 400 |
| 0.30 | 0.9975 | 0.9348 | 399 |
| 0.50 | 0.9825 | 0.9364 | 393 |
| 0.60 | 0.9650 | 0.9404 | 386 |
| 0.70 | 0.9525 | 0.9449 | 381 |
| 0.80 | 0.9375 | 0.9547 | 375 |
| 0.85 | 0.9300 | 0.9597 | 372 |
| 0.90 | 0.9200 | 0.9620 | 368 |
| 0.95 | 0.8900 | 0.9635 | 356 |
| 0.99 | 0.8525 | 0.9677 | 341 |

### All kinds

| Threshold | Coverage | Accuracy | Covered |
| ---: | ---: | ---: | ---: |
| 0.00 | 1.0000 | 0.9358 | 1200 |
| 0.30 | 0.9992 | 0.9358 | 1199 |
| 0.50 | 0.9942 | 0.9363 | 1193 |
| 0.60 | 0.9675 | 0.9500 | 1161 |
| 0.70 | 0.9383 | 0.9583 | 1126 |
| 0.80 | 0.9075 | 0.9725 | 1089 |
| 0.85 | 0.8900 | 0.9775 | 1068 |
| 0.90 | 0.8608 | 0.9816 | 1033 |
| 0.95 | 0.8117 | 0.9846 | 974 |
| 0.99 | 0.3683 | 0.9729 | 442 |

## Latency

Median 57.1 ms, p95 91.1 ms, over 400 requests.

## Cost

Per 1,000 decisions: 0.0293118, in the currency of the prices and reported costs.

Cost came from the provider for 0 items, from a price for 400, and was unknown for 0. Cached responses left out: 0.

Prices used, per million tokens:

- jev-1.13.0: 0.042 input, 0 output, read 2026-10-17 from TypeSafe official pricing page, supplied by the maintainer

## How to read this

Every question with an expected answer is scored against it. The number a threshold gates on is the confidence of the answer: for a Noul question it is the larger of p, the model's probability that the answer is yes, and 1 - p, so it is never below 0.5; for a Choice or Score question it is the confidence the model reports, which can be lower. Use the sweep of each kind, not the one over all kinds, to choose a threshold.

Answers are sorted by confidence and split into up to ten bins of equal size, give or take one answer. In a well-calibrated model the accuracy of each bin is close to its mean confidence. ECE, the expected calibration error, is the gap between the two averaged over the bins, weighted by how many answers each holds: 0 is perfect, and larger is worse. The equal-width column does the same over ten bins of equal confidence width, which can leave bins empty or crowded.

The base rate is what you would score by always giving the most common expected answer for each question. It is worked out per question, over the answered questions only, from the same answers it is compared with, so it is high when a question has few answers: a question answered once always matches its base rate. An accuracy that does not beat it says little.

A failed request is an abstention. Accuracy, base rate and calibration use the answers that came back; the sweep's coverage is a share of all scored questions, so a failed request counts as not covered. An InvalidResponse request was served and billed, so its cost is in the cost and its model in the responses served, but like every failed request it is left out of latency. Responses served from a cache are left out of latency and cost.

With few answers every figure moves a lot: a bin of three answers can only show an accuracy of 0, a third, two thirds or 1. Treat results from a small sample as a first look, not a measurement.
