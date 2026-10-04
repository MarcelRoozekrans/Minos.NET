# Benchmark workload

Every client in the comparison calls the same local mock and asks the same questions. The mock serves
[`response.json`](response.json), so every client parses the same answers.

## Source recording

`response.json` is entry 8 of `samples/ZeroAlloc.Jev.Samples.IntentRouting/recordings.json`, zero-based, with
`requestHash` beginning `64ae16d6`. It is the `responseBody` value, decoded to plain JSON and pretty-printed. The
recording was made on 2026-10-02 against model `typesafe/jev-1.13-20260917`. The body has the shape
`{ model, answers, usage, id, provider }`, which the official SDKs read as a `/v1/systemone` result.

## State

The document every client sends, which the mock ignores: `Is my booking to Rome still on? I fly tonight.`

## Question set

Two questions, with these exact keys.

| Key | Type | Instructions | Options |
|---|---|---|---|
| `intent` | Choice | `What does the traveller want?` | see below |
| `travels_within24_hours` | Noul | `Does the request mention travelling within the next 24 hours?` | none |

Choice options for `intent`, in this order:

| Option | Description |
|---|---|
| `look_up_booking` | `Wants to see or look up an existing booking` |
| `change_booking` | `Wants to change dates, names, seats or luggage on a booking` |
| `dispute_charge` | `Disputes a charge or asks for money back` |
| `other` | `Anything else` |

There are no Score questions, so no levels. Every included client can express Choice and Noul questions.

## Expected answers

Every harness asserts these values after each warm-up call, and on the first measured call.

| Answer | Value |
|---|---|
| `intent.choice` | `look_up_booking` |
| `intent.confidence` | `0.99` |
| `intent.probabilities` | `look_up_booking` 0.99, `change_booking` 0, `other` 0.01, `dispute_charge` 0 |
| `travels_within24_hours.noul` | `0.86` |
| `model` | `typesafe/jev-1.13-20260917` |
