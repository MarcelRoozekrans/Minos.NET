# Banking77 sample

A labelled sample for `Minos.NET.Evaluation`, built from the Banking77 test messages. It feeds the calibration reports
that compare providers. The format is described in [docs/datasets.md](../../docs/datasets.md).

## Files

| File | Content |
| --- | --- |
| `banking77-minos-v1.jsonl` | 400 items: all 40 test messages of each of 10 intents, with three questions. |
| `banking77-urgency-v1.jsonl` | 60 items: 6 messages per intent, with one urgency question. |
| `source/test-subset.csv` | The 400 source rows of the 10 intents, in the original order. It has the original columns `text` and `category`, plus a `row` column with the 1-based data row of the message in the upstream `test.csv`. |
| `urgency-labels.csv` | The urgency level of each of the 60 messages, keyed by the SHA-256 of the message text. |
| `build.py` | Builds both JSONL files. `--download` refetches `source/test-subset.csv` from the pinned commit. |
| `test_build.py` | Unit tests for the build. CI runs them. |

Run `python3 datasets/banking77/build.py` to rebuild. The output is byte-stable: UTF-8 without a BOM, LF line endings,
and a fixed key order.

## Source

- Repository: [PolyAI-LDN/task-specific-datasets](https://github.com/PolyAI-LDN/task-specific-datasets), file
  `banking_data/test.csv`, header `text,category`, 3,080 rows with 40 per intent.
- Pinned commit: `57ec275d8078af65b7731c2a98be812d844a6d6b`
- URL: <https://raw.githubusercontent.com/PolyAI-LDN/task-specific-datasets/57ec275d8078af65b7731c2a98be812d844a6d6b/banking_data/test.csv>
- Paper: Casanueva et al., "Efficient Intent Detection with Dual Sentence Encoders", 2020.
- License: the messages and intent names are CC BY 4.0, by PolyAI. See [LICENSE](LICENSE).

The `source` member of an item is `banking77/test#<row>`, where `<row>` is the 1-based data row of the message in the
upstream `banking_data/test.csv` (the header is not counted), so an item can be traced to the original file. `--download`
computes the number from the full file and stores it in the `row` column. The id of an item is `b77-<intent>-<nn>`, where `nn` is the
1-based position of the message within its intent, in file order.

## The intents

In the order of the options of the `intent` question:

1. `card_arrival`
2. `card_delivery_estimate`
3. `card_linking`
4. `card_not_working`
5. `lost_or_stolen_card`
6. `declined_card_payment`
7. `pending_card_payment`
8. `card_payment_fee_charged`
9. `activate_my_card`
10. `change_pin`

## The questions of `banking77-minos-v1.jsonl`

| Key | Kind | Question | True for | True rate |
| --- | --- | --- | --- | --- |
| `intent` | Choice | Which topic is this customer message about? Ten options, each with one short description. | | |
| `card_not_arrived` | Noul | Is the message about the delivery or arrival of a card? | `card_arrival`, `card_delivery_estimate` | 0.20 |
| `about_a_payment` | Noul | Is the message about a card payment being declined, pending, or charged a fee? | `declined_card_payment`, `pending_card_payment`, `card_payment_fee_charged` | 0.30 |

The expected answers of the two Nouls follow from the human intent label of the message, so a few messages fit the
wording less well than their label suggests. The four known cases:

| Id | Question | Label | Why it is noisy |
| --- | --- | --- | --- |
| `b77-card_not_working-18` | `about_a_payment` | false | False by intent, though the message says the card was declined while eating. |
| `b77-card_not_working-36` | `about_a_payment` | false | False by intent, though the message says the card does not accept any transaction, which describes a declined payment. |
| `b77-card_arrival-01` | `card_not_arrived` | true | True by intent, but "How do I locate my card?" is a weak fit for a question about delivery or arrival. |
| `b77-lost_or_stolen_card-33` | `card_not_arrived` | false | False by intent, though the message asks for a new card to be shipped. |

The labels are kept as derived from the human intent label, so the true rates stay 0.20 and 0.30. Read the reports with
this known noise. The keys are pinned, and `card_not_arrived` now asks about delivery in general: it also covers delivery
time and speed questions, not only a card that is late.

## The urgency set

`banking77-urgency-v1.jsonl` holds the 6 messages of each intent with the lowest `sha256` of the UTF-8 text, so the
pick is stable. It has one Score question, `urgency`, with these levels:

| Level | Name | Rubric |
| --- | --- | --- |
| 0 | Routine | A question or a request for information, with nothing at risk. |
| 1 | Needs action soon | Something is not working or is pending, and the customer is blocked. |
| 2 | Urgent: money or card security at risk | A lost or stolen card, an unknown or fraudulent charge, or money that is missing. |

**Urgency labels drafted by Claude on 2026-10-17; awaiting the maintainer's spot-check of at least 15 before
Phase 8.4.** Until then the urgency set is a draft, and no report should treat its labels as a ground truth.

The examples of the rubric are not in the question text, so the question does not hint at an answer. With 6 messages
per intent picked by hash, the urgency is close to a function of the intent. Level 2 holds only lost or stolen card
messages, and the cases of the rubric about an unknown charge or missing money have no items. The urgency numbers are
therefore weak evidence for level 2.

The labels are the authored part of the sample. They are in `urgency-labels.csv`, with the columns `text_sha256` and
`urgency`, and the build fails when a selected message has no label or a label matches no message.

## Rebuilding from the source

```bash
python3 datasets/banking77/build.py             # reads source/
python3 datasets/banking77/build.py --download  # refetches source/test-subset.csv from the pinned commit first
python3 -m unittest discover -s datasets/banking77 -p "test_build.py"
```

Only the Python standard library is used.
