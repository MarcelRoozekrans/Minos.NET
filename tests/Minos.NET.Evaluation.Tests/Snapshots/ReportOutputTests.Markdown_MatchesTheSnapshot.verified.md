# Calibration report: fake jev-1.14, 2026-10-17

Dataset: fixture, 17 items.

Responses served by: no model named (10), jev-1.14-20261014 (4)

## Errors

3 of 17 requests failed: Server 2, Timeout 1. A failed request is an abstention.

## Summary

| Kind | Scored | Answered | Accuracy | Base rate | ECE | ECE, equal width |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Noul | 17 | 14 | 0.7143 | 0.7143 | 0.2121 | 0.2121 |
| Choice | 3 | 2 | 0.5000 | 1.0000 | 0.6500 | 0.6500 |
| Score | 3 | 2 | 0.5000 | 1.0000 | 0.4000 | 0.4000 |
| All kinds | 23 | 18 | 0.6667 | 0.7778 | 0.1761 | 0.1983 |

## Reliability

### Noul

| Bin | Answers | Mean confidence | Accuracy |
| ---: | ---: | ---: | ---: |
| 1 | 2 | 0.5750 | 0.5000 |
| 2 | 2 | 0.6250 | 0.0000 |
| 3 | 2 | 0.7250 | 1.0000 |
| 4 | 2 | 0.8250 | 0.5000 |
| 5 | 1 | 0.9000 | 1.0000 |
| 6 | 1 | 0.9000 | 1.0000 |
| 7 | 1 | 0.9200 | 1.0000 |
| 8 | 1 | 0.9500 | 1.0000 |
| 9 | 1 | 0.9700 | 1.0000 |
| 10 | 1 | 0.9900 | 1.0000 |

### Choice

| Bin | Answers | Mean confidence | Accuracy |
| ---: | ---: | ---: | ---: |
| 1 | 1 | 0.4000 | 1.0000 |
| 2 | 1 | 0.7000 | 0.0000 |

### Score

| Bin | Answers | Mean confidence | Accuracy |
| ---: | ---: | ---: | ---: |
| 1 | 1 | 0.6000 | 0.0000 |
| 2 | 1 | 0.8000 | 1.0000 |

### All kinds

| Bin | Answers | Mean confidence | Accuracy |
| ---: | ---: | ---: | ---: |
| 1 | 2 | 0.4750 | 0.5000 |
| 2 | 2 | 0.6000 | 0.5000 |
| 3 | 2 | 0.6250 | 0.0000 |
| 4 | 2 | 0.7000 | 0.5000 |
| 5 | 2 | 0.7750 | 0.5000 |
| 6 | 2 | 0.8250 | 1.0000 |
| 7 | 2 | 0.9000 | 1.0000 |
| 8 | 2 | 0.9350 | 1.0000 |
| 9 | 1 | 0.9700 | 1.0000 |
| 10 | 1 | 0.9900 | 1.0000 |

## Threshold sweep

The full sweep, from 0.00 to 0.99 in steps of 0.01, is in the JSON.

### Noul

| Threshold | Coverage | Accuracy | Covered |
| ---: | ---: | ---: | ---: |
| 0.00 | 0.8235 | 0.7143 | 14 |
| 0.30 | 0.8235 | 0.7143 | 14 |
| 0.50 | 0.8235 | 0.7143 | 14 |
| 0.60 | 0.7647 | 0.7692 | 13 |
| 0.70 | 0.5882 | 0.9000 | 10 |
| 0.80 | 0.4706 | 0.8750 | 8 |
| 0.85 | 0.4118 | 1.0000 | 7 |
| 0.90 | 0.3529 | 1.0000 | 6 |
| 0.95 | 0.1765 | 1.0000 | 3 |
| 0.99 | 0.0588 | 1.0000 | 1 |

### Choice

| Threshold | Coverage | Accuracy | Covered |
| ---: | ---: | ---: | ---: |
| 0.00 | 0.6667 | 0.5000 | 2 |
| 0.30 | 0.6667 | 0.5000 | 2 |
| 0.50 | 0.3333 | 0.0000 | 1 |
| 0.60 | 0.3333 | 0.0000 | 1 |
| 0.70 | 0.3333 | 0.0000 | 1 |
| 0.80 | 0.0000 | n/a | 0 |
| 0.85 | 0.0000 | n/a | 0 |
| 0.90 | 0.0000 | n/a | 0 |
| 0.95 | 0.0000 | n/a | 0 |
| 0.99 | 0.0000 | n/a | 0 |

### Score

| Threshold | Coverage | Accuracy | Covered |
| ---: | ---: | ---: | ---: |
| 0.00 | 0.6667 | 0.5000 | 2 |
| 0.30 | 0.6667 | 0.5000 | 2 |
| 0.50 | 0.6667 | 0.5000 | 2 |
| 0.60 | 0.6667 | 0.5000 | 2 |
| 0.70 | 0.3333 | 1.0000 | 1 |
| 0.80 | 0.3333 | 1.0000 | 1 |
| 0.85 | 0.0000 | n/a | 0 |
| 0.90 | 0.0000 | n/a | 0 |
| 0.95 | 0.0000 | n/a | 0 |
| 0.99 | 0.0000 | n/a | 0 |

### All kinds

| Threshold | Coverage | Accuracy | Covered |
| ---: | ---: | ---: | ---: |
| 0.00 | 0.7826 | 0.6667 | 18 |
| 0.30 | 0.7826 | 0.6667 | 18 |
| 0.50 | 0.7391 | 0.6471 | 17 |
| 0.60 | 0.6957 | 0.6875 | 16 |
| 0.70 | 0.5217 | 0.8333 | 12 |
| 0.80 | 0.3913 | 0.8889 | 9 |
| 0.85 | 0.3043 | 1.0000 | 7 |
| 0.90 | 0.2609 | 1.0000 | 6 |
| 0.95 | 0.1304 | 1.0000 | 3 |
| 0.99 | 0.0435 | 1.0000 | 1 |

## Latency

Median 700 ms, p95 1300 ms, over 14 requests.

## Cost

Per 1,000 decisions: 0.5210.

Cost came from the provider for 4 items, from a price for 4, and was unknown for 6. Cached responses, which are left out: 0.

Prices used, per million tokens:

- jev-1.14: 0.042 input, 0 output, read 2026-10-17 from OpenRouter listing

## How to read this

Every answer is scored against the expected answer in the dataset. The number a threshold gates on is the confidence of the answer: for a Noul question it is the larger of p and 1 - p, so it is never below 0.5; for a Choice or Score question it is the confidence the model reports, which can be lower. Use the sweep of each kind, not the one over all kinds, to choose a threshold.

Answers are sorted by confidence and cut into up to ten bins that each hold the same number of answers. In a well-calibrated model the accuracy of each bin is close to its mean confidence. ECE, the expected calibration error, is the gap between the two averaged over the bins, weighted by how many answers each holds: 0 is perfect, and larger is worse. The equal-width column does the same over ten bins of equal confidence width, which can leave bins empty or crowded.

The base rate is what you would score by always giving the most common expected answer for each question. An accuracy that does not beat it says little.

A failed request is an abstention. Accuracy, base rate and calibration use the answers that came back; the sweep's coverage is a share of all scored questions, so a failed request counts as not covered. Responses served from a cache are left out of latency and cost.

With few answers every figure moves a lot: a bin of three answers can only show an accuracy of 0, a third, two thirds or 1. Treat results from a small sample as a first look, not a measurement.
