# Calibration report: openrouter cloudflare/clef-flash, 2026-10-17

Dataset: banking77-minos-v1, 400 items.

Responses served by: cloudflare/clef-flash (400)

## Errors

No request failed.

## Summary

| Kind | Scored | Answered | Accuracy | Base rate | ECE | ECE, equal width |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Noul | 800 | 800 | 0.9900 | 0.7500 | 0.0421 | 0.0449 |
| Choice | 400 | 400 | 0.9725 | 0.1000 | 0.0598 | 0.0598 |
| All kinds | 1200 | 1200 | 0.9842 | 0.5333 | 0.0480 | 0.0497 |

## Reliability

### Noul

| Bin | Answers | Mean confidence | Accuracy |
| ---: | ---: | ---: | ---: |
| 1 | 80 | 0.8080 | 0.9125 |
| 2 | 80 | 0.9000 | 1.0000 |
| 3 | 80 | 0.9299 | 0.9875 |
| 4 | 80 | 0.9530 | 1.0000 |
| 5 | 80 | 0.9685 | 1.0000 |
| 6 | 80 | 0.9758 | 1.0000 |
| 7 | 80 | 0.9809 | 1.0000 |
| 8 | 80 | 0.9843 | 1.0000 |
| 9 | 80 | 0.9883 | 1.0000 |
| 10 | 80 | 0.9904 | 1.0000 |

### Choice

| Bin | Answers | Mean confidence | Accuracy |
| ---: | ---: | ---: | ---: |
| 1 | 40 | 0.6317 | 0.8250 |
| 2 | 40 | 0.8446 | 0.9000 |
| 3 | 40 | 0.9045 | 1.0000 |
| 4 | 40 | 0.9306 | 1.0000 |
| 5 | 40 | 0.9463 | 1.0000 |
| 6 | 40 | 0.9595 | 1.0000 |
| 7 | 40 | 0.9679 | 1.0000 |
| 8 | 40 | 0.9745 | 1.0000 |
| 9 | 40 | 0.9801 | 1.0000 |
| 10 | 40 | 0.9874 | 1.0000 |

### All kinds

| Bin | Answers | Mean confidence | Accuracy |
| ---: | ---: | ---: | ---: |
| 1 | 120 | 0.7398 | 0.8583 |
| 2 | 120 | 0.8884 | 0.9917 |
| 3 | 120 | 0.9211 | 1.0000 |
| 4 | 120 | 0.9446 | 0.9917 |
| 5 | 120 | 0.9608 | 1.0000 |
| 6 | 120 | 0.9710 | 1.0000 |
| 7 | 120 | 0.9773 | 1.0000 |
| 8 | 120 | 0.9821 | 1.0000 |
| 9 | 120 | 0.9866 | 1.0000 |
| 10 | 120 | 0.9902 | 1.0000 |

## Threshold sweep

The full sweep, from 0.00 to 0.99 in steps of 0.01, is in the JSON.

### Noul

| Threshold | Coverage | Accuracy | Covered |
| ---: | ---: | ---: | ---: |
| 0.00 | 1.0000 | 0.9900 | 800 |
| 0.30 | 1.0000 | 0.9900 | 800 |
| 0.50 | 1.0000 | 0.9900 | 800 |
| 0.60 | 0.9950 | 0.9937 | 796 |
| 0.70 | 0.9875 | 0.9962 | 790 |
| 0.80 | 0.9712 | 0.9974 | 777 |
| 0.85 | 0.9475 | 0.9987 | 758 |
| 0.90 | 0.8462 | 0.9985 | 677 |
| 0.95 | 0.6637 | 1.0000 | 531 |
| 0.99 | 0.0600 | 1.0000 | 48 |

### Choice

| Threshold | Coverage | Accuracy | Covered |
| ---: | ---: | ---: | ---: |
| 0.00 | 1.0000 | 0.9725 | 400 |
| 0.30 | 1.0000 | 0.9725 | 400 |
| 0.50 | 0.9700 | 0.9820 | 388 |
| 0.60 | 0.9575 | 0.9869 | 383 |
| 0.70 | 0.9500 | 0.9895 | 380 |
| 0.80 | 0.8975 | 0.9916 | 359 |
| 0.85 | 0.8475 | 0.9941 | 339 |
| 0.90 | 0.7750 | 1.0000 | 310 |
| 0.95 | 0.5300 | 1.0000 | 212 |
| 0.99 | 0.0300 | 1.0000 | 12 |

### All kinds

| Threshold | Coverage | Accuracy | Covered |
| ---: | ---: | ---: | ---: |
| 0.00 | 1.0000 | 0.9842 | 1200 |
| 0.30 | 1.0000 | 0.9842 | 1200 |
| 0.50 | 0.9900 | 0.9874 | 1188 |
| 0.60 | 0.9825 | 0.9915 | 1179 |
| 0.70 | 0.9750 | 0.9940 | 1170 |
| 0.80 | 0.9467 | 0.9956 | 1136 |
| 0.85 | 0.9142 | 0.9973 | 1097 |
| 0.90 | 0.8225 | 0.9990 | 987 |
| 0.95 | 0.6192 | 1.0000 | 743 |
| 0.99 | 0.0500 | 1.0000 | 60 |

## Latency

Median 221.5 ms, p95 462.1 ms, over 400 requests.

## Cost

Per 1,000 decisions: 0.0161494, in the currency of the prices and reported costs.

Cost came from the provider for 400 items, from a price for 0, and was unknown for 0. Cached responses left out: 0.

Prices used: none.

## How to read this

Every question with an expected answer is scored against it. The number a threshold gates on is the confidence of the answer: for a Noul question it is the larger of p, the model's probability that the answer is yes, and 1 - p, so it is never below 0.5; for a Choice or Score question it is the confidence the model reports, which can be lower. Use the sweep of each kind, not the one over all kinds, to choose a threshold.

Answers are sorted by confidence and split into up to ten bins of equal size, give or take one answer. In a well-calibrated model the accuracy of each bin is close to its mean confidence. ECE, the expected calibration error, is the gap between the two averaged over the bins, weighted by how many answers each holds: 0 is perfect, and larger is worse. The equal-width column does the same over ten bins of equal confidence width, which can leave bins empty or crowded.

The base rate is what you would score by always giving the most common expected answer for each question. It is worked out per question, over the answered questions only, from the same answers it is compared with, so it is high when a question has few answers: a question answered once always matches its base rate. An accuracy that does not beat it says little.

A failed request is an abstention. Accuracy, base rate and calibration use the answers that came back; the sweep's coverage is a share of all scored questions, so a failed request counts as not covered. An InvalidResponse request was served and billed, so its cost is in the cost and its model in the responses served, but like every failed request it is left out of latency. Responses served from a cache are left out of latency and cost.

With few answers every figure moves a lot: a bin of three answers can only show an accuracy of 0, a third, two thirds or 1. Treat results from a small sample as a first look, not a measurement.
