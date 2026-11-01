# Shadow-replay sample

This sample replays a labelled dataset through the recorder's [shadow mode](../../docs/recording.md#shadow-mode). A hosted
primary decides every item. A second model, the candidate, is asked the same questions on the side, and both sets of
answers are written to a JSONL records file. The records are what `minos shadow-report` reads.

The traffic is replayed Banking77 customer messages, not live traffic. Each item's outcome comes from its dataset labels,
and the outcome's source is `banking77-label`. The decision id of each record is the dataset item's `Id`.

The sample uses project references, so it runs the code of its own commit. That code matches the Minos.NET.Recording and
Minos.NET.Evaluation packages from version 1.8.0 on.

## What it does

For each item, in dataset order, the sample:

1. asks the primary, and records its decision;
2. adds the item's labels as the confirmed outcome;
3. waits for the candidate's shadow write, for at most the candidate's 60 second timeout plus a margin of 5 seconds.

The candidate runs one call at a time with a daily cap of 400, so with the wait no decision is skipped as busy. For that
reason the sample takes at most 400 items: a larger dataset exits with code 2 before anything is sent, because an item past
the cap would not be shadowed and would still wait the full 65 seconds. A line is
logged for each item the primary failed and for every 25 items, never the message text.

## Run it

```
dotnet run --project samples/Minos.NET.Samples.ShadowReplay -- \
  --dataset datasets/banking77/banking77-minos-v1.jsonl \
  --config docs/reports/minos.json \
  --primary TypeSafe \
  --candidate Local \
  --records shadow-records.jsonl
```

All five options are required.

| Option | Meaning |
| --- | --- |
| `--dataset <jsonl>` | The labelled dataset, one item per line. |
| `--config <minos.json>` | The config file. Each section under `Minos:Providers` is a client. Environment variables such as `Minos__Providers__Local__Model` override the file, as in `minos calibrate`. |
| `--primary <section>` | The section of the client whose decisions are recorded. |
| `--candidate <section>` | The section of the client that shadows it. |
| `--records <file>` | The JSONL records file. |

The sample starts the records file fresh. If `--records` points at an existing file, the sample deletes it before the run,
because the records sink appends and an earlier run's lines would mix into this one.

A hosted primary run is billed: the `TypeSafe` section needs `TYPESAFE_API_KEY`, and every item is one billed request. The
candidate in `docs/reports/minos.json` is the `Local` section, a local server at `http://127.0.0.1:8000/`, which must be
running and bills nothing.

## Output and exit codes

At the end the sample prints one line:

```
items=<n> decided=<n> primary_failed=<n> shadowed=<n> waits_run_out=<n>
```

| Code | Meaning |
| --- | --- |
| 0 | The primary decided at least one item. |
| 1 | It decided none. |
| 2 | An option is missing, or the dataset, the config or the records file cannot be used, or the dataset has more than 400 items. |
| 130 | Ctrl+C or SIGTERM. The records written so far are kept. |

Read the records with the shadow report, described in [docs/reports/README.md](../../docs/reports/README.md). The report
shows how the candidate's answers compare with the primary's and with the labels; it does not say whether to switch.
