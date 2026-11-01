# Shadow report

Decisions read: 400. Shadowed: 400. Only the shadowed decisions are compared, and each side below covers those.

## Primary

Responses served by: jev-1.14.0 (400)

Errors: No request failed.

Refused questions: 0.

Latency: Median 50.5 ms, p95 82.4 ms, over 400 requests.

Cost: Per 1,000 decisions: 0.0293118, in the currency of the prices and reported costs.

Cost came from the provider for 0 items, from a price for 400, and was unknown for 0. Cached responses left out: 0.

Prices used, per million tokens:

- jev-1.14.0: 0.042 input, 0 output, read 2026-11-01 from TypeSafe official pricing page, as supplied by the maintainer on 2026-11-01

## Candidate

Responses served by: Mapika/decider-2b-GGUF:Q4_K_M (400)

Errors: No request failed.

Refused questions: 0.

Latency: Median 4043.8 ms, p95 4509.7 ms, over 400 requests.

Cost: Per 1,000 decisions: 0, in the currency of the prices and reported costs.

Cost came from the provider for 0 items, from a price for 400, and was unknown for 0. Cached responses left out: 0.

Prices used, per million tokens:

- Mapika/decider-2b-GGUF:Q4_K_M: 0 input, 0 output, read 2026-10-17 from runs on your own hardware; no per-token price

## Agreement

| Question | Kind | Compared | Agreement | Mean level difference | Primary only | Candidate only |
| --- | --- | ---: | ---: | ---: | ---: | ---: |
| intent | Choice | 400 | 0.9600 | n/a | 0 | 0 |
| card_not_arrived | Noul | 400 | 0.9300 | n/a | 0 | 0 |
| about_a_payment | Noul | 400 | 0.9450 | n/a | 0 | 0 |

## Confusion

The primary's answers are the rows, the candidate's the columns.

### intent, Choice

| Primary \ Candidate | card_arrival | card_delivery_estimate | card_linking | card_not_working | lost_or_stolen_card | declined_card_payment | pending_card_payment | card_payment_fee_charged | activate_my_card | change_pin |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| card_arrival | 36 | 0 | 1 | 1 | 1 | 0 | 0 | 0 | 0 | 0 |
| card_delivery_estimate | 5 | 38 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| card_linking | 0 | 0 | 31 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| card_not_working | 0 | 0 | 1 | 37 | 0 | 0 | 0 | 0 | 0 | 0 |
| lost_or_stolen_card | 0 | 0 | 0 | 0 | 45 | 0 | 0 | 0 | 0 | 0 |
| declined_card_payment | 0 | 0 | 0 | 1 | 0 | 42 | 0 | 0 | 0 | 0 |
| pending_card_payment | 2 | 0 | 0 | 0 | 0 | 1 | 36 | 0 | 0 | 0 |
| card_payment_fee_charged | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 40 | 0 | 0 |
| activate_my_card | 0 | 0 | 0 | 1 | 2 | 0 | 0 | 0 | 39 | 0 |
| change_pin | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 40 |

## Signals

The signal outcomes of the compared decisions, per question: agreed when the two sides gave that question the same answer, disagreed when their answers differed. Signals are never expected answers.

No compared decision has a signal outcome.

## Calibration

Both sides are measured on the same 1200 questions of 400 decisions: those both sides answered and a confirmed outcome answered with an answer that fits the question. 0 confirmed keys were answered by the primary only and 0 by the candidate only; they are not calibrated.

### Primary

| Kind | Scored | Answered | Accuracy | Base rate | ECE | ECE, equal width |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Noul | 800 | 800 | 0.9287 | 0.7500 | 0.0387 | 0.0390 |
| Choice | 400 | 400 | 0.9250 | 0.1000 | 0.0387 | 0.0427 |
| All kinds | 1200 | 1200 | 0.9275 | 0.5333 | 0.0262 | 0.0166 |

#### Reliability, Noul

| Bin | Answers | Mean confidence | Accuracy |
| ---: | ---: | ---: | ---: |
| 1 | 80 | 0.6451 | 0.5000 |
| 2 | 80 | 0.8646 | 0.8125 |
| 3 | 80 | 0.9464 | 0.9875 |
| 4 | 80 | 0.9647 | 1.0000 |
| 5 | 80 | 0.9700 | 1.0000 |
| 6 | 80 | 0.9766 | 1.0000 |
| 7 | 80 | 0.9800 | 0.9875 |
| 8 | 80 | 0.9800 | 1.0000 |
| 9 | 80 | 0.9800 | 1.0000 |
| 10 | 80 | 0.9871 | 1.0000 |

#### Reliability, Choice

| Bin | Answers | Mean confidence | Accuracy |
| ---: | ---: | ---: | ---: |
| 1 | 40 | 0.6832 | 0.5750 |
| 2 | 40 | 0.9662 | 0.8500 |
| 3 | 40 | 0.9880 | 0.9000 |
| 4 | 40 | 0.9992 | 0.9750 |
| 5 | 40 | 1.0000 | 0.9500 |
| 6 | 40 | 1.0000 | 1.0000 |
| 7 | 40 | 1.0000 | 1.0000 |
| 8 | 40 | 1.0000 | 1.0000 |
| 9 | 40 | 1.0000 | 1.0000 |
| 10 | 40 | 1.0000 | 1.0000 |

#### Reliability, All kinds

| Bin | Answers | Mean confidence | Accuracy |
| ---: | ---: | ---: | ---: |
| 1 | 120 | 0.6491 | 0.5250 |
| 2 | 120 | 0.8972 | 0.8667 |
| 3 | 120 | 0.9594 | 0.9750 |
| 4 | 120 | 0.9700 | 0.9833 |
| 5 | 120 | 0.9791 | 0.9833 |
| 6 | 120 | 0.9800 | 1.0000 |
| 7 | 120 | 0.9808 | 0.9917 |
| 8 | 120 | 0.9931 | 0.9667 |
| 9 | 120 | 1.0000 | 0.9833 |
| 10 | 120 | 1.0000 | 1.0000 |

### Candidate

| Kind | Scored | Answered | Accuracy | Base rate | ECE | ECE, equal width |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Noul | 800 | 800 | 0.9363 | 0.7500 | 0.0613 | 0.0645 |
| Choice | 400 | 400 | 0.9075 | 0.1000 | 0.0507 | 0.0612 |
| All kinds | 1200 | 1200 | 0.9267 | 0.5333 | 0.0510 | 0.0423 |

#### Reliability, Noul

| Bin | Answers | Mean confidence | Accuracy |
| ---: | ---: | ---: | ---: |
| 1 | 80 | 0.9760 | 0.8375 |
| 2 | 80 | 1.0000 | 0.9875 |
| 3 | 80 | 1.0000 | 0.9750 |
| 4 | 80 | 1.0000 | 0.7625 |
| 5 | 80 | 1.0000 | 0.9625 |
| 6 | 80 | 1.0000 | 0.9375 |
| 7 | 80 | 1.0000 | 0.9250 |
| 8 | 80 | 1.0000 | 1.0000 |
| 9 | 80 | 1.0000 | 1.0000 |
| 10 | 80 | 1.0000 | 0.9750 |

#### Reliability, Choice

| Bin | Answers | Mean confidence | Accuracy |
| ---: | ---: | ---: | ---: |
| 1 | 40 | 0.5620 | 0.4750 |
| 2 | 40 | 0.7857 | 0.8250 |
| 3 | 40 | 0.8492 | 0.9000 |
| 4 | 40 | 0.8875 | 0.9250 |
| 5 | 40 | 0.9121 | 0.9750 |
| 6 | 40 | 0.9276 | 1.0000 |
| 7 | 40 | 0.9400 | 0.9750 |
| 8 | 40 | 0.9488 | 1.0000 |
| 9 | 40 | 0.9576 | 1.0000 |
| 10 | 40 | 0.9718 | 1.0000 |

#### Reliability, All kinds

| Bin | Answers | Mean confidence | Accuracy |
| ---: | ---: | ---: | ---: |
| 1 | 120 | 0.7241 | 0.7250 |
| 2 | 120 | 0.9059 | 0.9667 |
| 3 | 120 | 0.9473 | 0.9917 |
| 4 | 120 | 0.9875 | 0.9000 |
| 5 | 120 | 1.0000 | 0.9917 |
| 6 | 120 | 1.0000 | 0.8250 |
| 7 | 120 | 1.0000 | 0.9583 |
| 8 | 120 | 1.0000 | 0.9250 |
| 9 | 120 | 1.0000 | 1.0000 |
| 10 | 120 | 1.0000 | 0.9833 |

## How to read this

The candidate answered the same questions as the primary, for the same decisions, in the background. Agreement is the share of the decisions in which both answered a question that they answered the same way: a Noul answer counts as yes at a probability of 0.5 or more, a Choice answer by its option, a Score answer by its level. For a Score question the mean level difference shows how far apart they were. Agreement needs no confirmed outcome, so it says how alike the two are, not which one is right.

Calibration says which one is right, and how well each one's confidence matches it. It counts only the questions both sides answered and a person confirmed, so the two are measured on the same items; the figures mean what they mean in a calibration report.

A question with fewer than 30 comparisons is marked small: with so few, every figure moves a lot. Treat it as a first look, not a measurement.
