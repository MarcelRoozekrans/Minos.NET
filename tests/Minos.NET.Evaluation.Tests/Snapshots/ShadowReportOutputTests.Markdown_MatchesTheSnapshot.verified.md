# Shadow report

Decisions read: 5. Shadowed: 4. Only the shadowed decisions are compared, and each side below covers those.

## Primary

Responses served by: no model named (4)

Errors: No request failed.

Refused questions: 0.

Latency: Median 212.5 ms, p95 212.5 ms, over 4 requests.

Cost: Per 1,000 decisions: n/a, in the currency of the prices and reported costs.

Cost came from the provider for 0 items, from a price for 0, and was unknown for 4. Cached responses left out: 0.

Prices used: none.

## Candidate

Responses served by: candidate-1 (3)

Errors: 1 of 4 requests failed: Timeout 1. A failed request is an abstention.

Refused questions: 0.

Verbalized probabilities: 3 answers have probabilities the model stated in its text, not a measured distribution. Agreement and calibration include them.

Latency: Median 700 ms, p95 900 ms, over 3 requests.

Cost: Per 1,000 decisions: 3, in the currency of the prices and reported costs.

Cost came from the provider for 0 items, from a price for 3, and was unknown for 0. Cached responses left out: 0.

Prices used, per million tokens:

- candidate-1: 2 input, 10 output, read 2026-10-30 from provider listing

## Agreement

| Question | Kind | Compared | Agreement | Mean level difference | Primary only | Candidate only |
| --- | --- | ---: | ---: | ---: | ---: | ---: |
| urgent | Noul | 3 (small: n < 30) | 0.6667 | n/a | 1 | 0 |
| queue | Choice | 3 (small: n < 30) | 0.6667 | n/a | 1 | 0 |
| mood | Score | 3 (small: n < 30) | 0.3333 | 1.0000 | 0 | 0 |

## Confusion

The primary's answers are the rows, the candidate's the columns.

### queue, Choice (small: n < 30)

| Primary \ Candidate | billing | technical | sales |
| --- | ---: | ---: | ---: |
| billing | 0 | 0 | 1 |
| technical | 0 | 1 | 0 |
| sales | 0 | 0 | 1 |

### mood, Score (small: n < 30)

| Primary \ Candidate | 0 | 1 | 2 | 3 | 4 |
| --- | ---: | ---: | ---: | ---: | ---: |
| 0 | 0 | 0 | 0 | 0 | 0 |
| 1 | 0 | 0 | 0 | 1 | 0 |
| 2 | 0 | 0 | 1 | 0 | 0 |
| 3 | 0 | 0 | 0 | 0 | 0 |
| 4 | 0 | 0 | 0 | 1 | 0 |

## Signals

The signal outcomes of the compared decisions, per question: agreed when the two sides gave the same answer to every question they both answered, disagreed otherwise. Signals are never expected answers.

| Question | Signal | Agreed | Disagreed |
| --- | --- | ---: | ---: |
| urgent | complained | 0 | 1 |
| urgent | opened | 1 | 0 |
| queue | complained | 0 | 1 |
| queue | opened | 1 | 0 |
| mood | complained | 0 | 1 |
| mood | opened | 1 | 0 |

## Calibration

Both sides are measured on the same 5 questions of 2 decisions: those both sides answered and a confirmed outcome answered with an answer that fits the question.

### Primary

| Kind | Scored | Answered | Accuracy | Base rate | ECE | ECE, equal width |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Noul | 2 | 2 | 1.0000 | 0.5000 | 0.2000 | 0.2000 |
| Choice | 2 | 2 | 1.0000 | 0.5000 | 0.2500 | 0.2500 |
| Score | 1 | 1 | 1.0000 | 1.0000 | 0.4000 | 0.4000 |
| All kinds | 5 | 5 | 1.0000 | 0.6000 | 0.2600 | 0.2600 |

#### Reliability, Noul

| Bin | Answers | Mean confidence | Accuracy |
| ---: | ---: | ---: | ---: |
| 1 | 1 | 0.7000 | 1.0000 |
| 2 | 1 | 0.9000 | 1.0000 |

#### Reliability, Choice

| Bin | Answers | Mean confidence | Accuracy |
| ---: | ---: | ---: | ---: |
| 1 | 1 | 0.7000 | 1.0000 |
| 2 | 1 | 0.8000 | 1.0000 |

#### Reliability, Score

| Bin | Answers | Mean confidence | Accuracy |
| ---: | ---: | ---: | ---: |
| 1 | 1 | 0.6000 | 1.0000 |

#### Reliability, All kinds

| Bin | Answers | Mean confidence | Accuracy |
| ---: | ---: | ---: | ---: |
| 1 | 1 | 0.6000 | 1.0000 |
| 2 | 1 | 0.7000 | 1.0000 |
| 3 | 1 | 0.7000 | 1.0000 |
| 4 | 1 | 0.8000 | 1.0000 |
| 5 | 1 | 0.9000 | 1.0000 |

### Candidate

| Kind | Scored | Answered | Accuracy | Base rate | ECE | ECE, equal width |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Noul | 2 | 2 | 0.5000 | 0.5000 | 0.4000 | 0.4000 |
| Choice | 2 | 2 | 0.5000 | 0.5000 | 0.3500 | 0.3500 |
| Score | 1 | 1 | 1.0000 | 1.0000 | 0.3000 | 0.3000 |
| All kinds | 5 | 5 | 0.6000 | 0.6000 | 0.3600 | 0.3600 |

#### Reliability, Noul

| Bin | Answers | Mean confidence | Accuracy |
| ---: | ---: | ---: | ---: |
| 1 | 1 | 0.6000 | 0.0000 |
| 2 | 1 | 0.8000 | 1.0000 |

#### Reliability, Choice

| Bin | Answers | Mean confidence | Accuracy |
| ---: | ---: | ---: | ---: |
| 1 | 1 | 0.6000 | 0.0000 |
| 2 | 1 | 0.9000 | 1.0000 |

#### Reliability, Score

| Bin | Answers | Mean confidence | Accuracy |
| ---: | ---: | ---: | ---: |
| 1 | 1 | 0.7000 | 1.0000 |

#### Reliability, All kinds

| Bin | Answers | Mean confidence | Accuracy |
| ---: | ---: | ---: | ---: |
| 1 | 1 | 0.6000 | 0.0000 |
| 2 | 1 | 0.6000 | 0.0000 |
| 3 | 1 | 0.7000 | 1.0000 |
| 4 | 1 | 0.8000 | 1.0000 |
| 5 | 1 | 0.9000 | 1.0000 |

## How to read this

The candidate answered the same questions as the primary, for the same decisions, in the background. Agreement is the share of the decisions in which both answered a question that they answered the same way: a Noul answer counts as yes at a probability of 0.5 or more, a Choice answer by its option, a Score answer by its level. For a Score question the mean level difference shows how far apart they were. Agreement needs no confirmed outcome, so it says how alike the two are, not which one is right.

Calibration says which one is right, and how well each one's confidence matches it. It counts only the questions both sides answered and a person confirmed, so the two are measured on the same items; the figures mean what they mean in a calibration report.

A question with fewer than 30 comparisons is marked small: with so few, every figure moves a lot. Treat it as a first look, not a measurement.
