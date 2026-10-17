# Calibration report: custom Mapika/decider-2b-GGUF:Q4_K_M, 2026-10-17

Dataset: banking77-minos-v1, 400 items.

Responses served by: Mapika/decider-2b-GGUF:Q4_K_M (400)

## Errors

No request failed.

## Summary

| Kind | Scored | Answered | Accuracy | Base rate | ECE | ECE, equal width |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Noul | 800 | 800 | 0.9387 | 0.7500 | 0.0592 | 0.0624 |
| Choice | 400 | 400 | 0.9225 | 0.1000 | 0.0199 | 0.0274 |
| All kinds | 1200 | 1200 | 0.9333 | 0.5333 | 0.0453 | 0.0471 |

## Reliability

### Noul

| Bin | Answers | Mean confidence | Accuracy |
| ---: | ---: | ---: | ---: |
| 1 | 80 | 0.6254 | 0.6125 |
| 2 | 80 | 0.7752 | 0.8125 |
| 3 | 80 | 0.8527 | 0.9750 |
| 4 | 80 | 0.8886 | 1.0000 |
| 5 | 80 | 0.9104 | 1.0000 |
| 6 | 80 | 0.9293 | 1.0000 |
| 7 | 80 | 0.9432 | 0.9875 |
| 8 | 80 | 0.9536 | 1.0000 |
| 9 | 80 | 0.9653 | 1.0000 |
| 10 | 80 | 0.9772 | 1.0000 |

### Choice

| Bin | Answers | Mean confidence | Accuracy |
| ---: | ---: | ---: | ---: |
| 1 | 40 | 0.5496 | 0.5250 |
| 2 | 40 | 0.8029 | 0.8500 |
| 3 | 40 | 0.9015 | 0.9250 |
| 4 | 40 | 0.9434 | 0.9750 |
| 5 | 40 | 0.9616 | 0.9500 |
| 6 | 40 | 0.9750 | 1.0000 |
| 7 | 40 | 0.9837 | 1.0000 |
| 8 | 40 | 0.9897 | 1.0000 |
| 9 | 40 | 0.9940 | 1.0000 |
| 10 | 40 | 0.9971 | 1.0000 |

### All kinds

| Bin | Answers | Mean confidence | Accuracy |
| ---: | ---: | ---: | ---: |
| 1 | 120 | 0.6001 | 0.5833 |
| 2 | 120 | 0.7815 | 0.8250 |
| 3 | 120 | 0.8644 | 0.9667 |
| 4 | 120 | 0.9005 | 0.9917 |
| 5 | 120 | 0.9257 | 0.9917 |
| 6 | 120 | 0.9432 | 0.9917 |
| 7 | 120 | 0.9558 | 1.0000 |
| 8 | 120 | 0.9684 | 0.9833 |
| 9 | 120 | 0.9803 | 1.0000 |
| 10 | 120 | 0.9936 | 1.0000 |

## Threshold sweep

The full sweep, from 0.00 to 0.99 in steps of 0.01, is in the JSON.

### Noul

| Threshold | Coverage | Accuracy | Covered |
| ---: | ---: | ---: | ---: |
| 0.00 | 1.0000 | 0.9387 | 800 |
| 0.30 | 1.0000 | 0.9387 | 800 |
| 0.50 | 1.0000 | 0.9387 | 800 |
| 0.60 | 0.9675 | 0.9548 | 774 |
| 0.70 | 0.9137 | 0.9713 | 731 |
| 0.80 | 0.8225 | 0.9924 | 658 |
| 0.85 | 0.7600 | 0.9984 | 608 |
| 0.90 | 0.6050 | 0.9979 | 484 |
| 0.95 | 0.2838 | 1.0000 | 227 |
| 0.99 | 0.0000 | n/a | 0 |

### Choice

| Threshold | Coverage | Accuracy | Covered |
| ---: | ---: | ---: | ---: |
| 0.00 | 1.0000 | 0.9225 | 400 |
| 0.30 | 1.0000 | 0.9225 | 400 |
| 0.50 | 0.9575 | 0.9452 | 383 |
| 0.60 | 0.9400 | 0.9495 | 376 |
| 0.70 | 0.9100 | 0.9643 | 364 |
| 0.80 | 0.8550 | 0.9766 | 342 |
| 0.85 | 0.8200 | 0.9787 | 328 |
| 0.90 | 0.7500 | 0.9900 | 300 |
| 0.95 | 0.6225 | 0.9920 | 249 |
| 0.99 | 0.2525 | 1.0000 | 101 |

### All kinds

| Threshold | Coverage | Accuracy | Covered |
| ---: | ---: | ---: | ---: |
| 0.00 | 1.0000 | 0.9333 | 1200 |
| 0.30 | 1.0000 | 0.9333 | 1200 |
| 0.50 | 0.9858 | 0.9408 | 1183 |
| 0.60 | 0.9583 | 0.9530 | 1150 |
| 0.70 | 0.9125 | 0.9689 | 1095 |
| 0.80 | 0.8333 | 0.9870 | 1000 |
| 0.85 | 0.7800 | 0.9915 | 936 |
| 0.90 | 0.6533 | 0.9949 | 784 |
| 0.95 | 0.3967 | 0.9958 | 476 |
| 0.99 | 0.0842 | 1.0000 | 101 |

## Latency

Median 6475 ms, p95 7183 ms, over 400 requests.

## Cost

Per 1,000 decisions: 0, in the currency of the prices and reported costs.

Cost came from the provider for 0 items, from a price for 400, and was unknown for 0. Cached responses left out: 0.

Prices used, per million tokens:

- Mapika/decider-2b-GGUF:Q4_K_M: 0 input, 0 output, read 2026-10-17 from runs on your own hardware; no per-token price

## How to read this

Every question with an expected answer is scored against it. The number a threshold gates on is the confidence of the answer: for a Noul question it is the larger of p, the model's probability that the answer is yes, and 1 - p, so it is never below 0.5; for a Choice or Score question it is the confidence the model reports, which can be lower. Use the sweep of each kind, not the one over all kinds, to choose a threshold.

Answers are sorted by confidence and split into up to ten bins of equal size, give or take one answer. In a well-calibrated model the accuracy of each bin is close to its mean confidence. ECE, the expected calibration error, is the gap between the two averaged over the bins, weighted by how many answers each holds: 0 is perfect, and larger is worse. The equal-width column does the same over ten bins of equal confidence width, which can leave bins empty or crowded.

The base rate is what you would score by always giving the most common expected answer for each question. It is worked out per question, over the answered questions only, from the same answers it is compared with, so it is high when a question has few answers: a question answered once always matches its base rate. An accuracy that does not beat it says little.

A failed request is an abstention. Accuracy, base rate and calibration use the answers that came back; the sweep's coverage is a share of all scored questions, so a failed request counts as not covered. An InvalidResponse request was served and billed, so its cost is in the cost and its model in the responses served, but like every failed request it is left out of latency. Responses served from a cache are left out of latency and cost.

With few answers every figure moves a lot: a bin of three answers can only show an accuracy of 0, a third, two thirds or 1. Treat results from a small sample as a first look, not a measurement.
