# Calibration reports

This folder holds the calibration runs of Minos and the files that produce them. It is not part of the docs site: the
site excludes `docs/reports/`, and the tool-written `report.md` files in it are read in the repository.

The six reports of the first calibration are committed, one folder for each provider and set.

## Layout

Each run has its own folder, `docs/reports/<provider>/<set>/`:

- `<provider>` is `typesafe`, `clef-flash` or `local`.
- `<set>` is `banking77` (`datasets/banking77/banking77-minos-v1.jsonl`) or `urgency`
  (`datasets/banking77/banking77-urgency-v1.jsonl`).

A new provider adds its slug to `Providers` and its runs to `RunIds` in `ReportFolderTests`.

Next to the folders sit the files that every run shares:

- `minos.json` is the run configuration. It has one section per provider: `TypeSafe`, `Clef` and `Local`. `Active` is
  `TypeSafe`. The file holds no API key; the keys come from the environment. The `Clef` section asks OpenRouter for
  zero data retention, so a request goes only to an endpoint that does not retain it.
- `prices.json` holds the prices applied to a report afterwards: the TypeSafe models `jev-1.13.0` and `jev-1.14.0`, and the local model at
  0 because it runs on your own hardware. Clef reports its own cost, so it has no entry.

## How a run is started

Runs are started by hand. No calibration runs on a push or on a schedule.

- `calibrate.yml` runs the hosted providers. Its inputs are `provider` (`typesafe` or `clef`) and `set` (`both`,
  `urgency` or `banking77`). Every dispatch is billed.
- The `calibrate` job of `local-systemone.yml` runs the local model. It is dispatch only, and it takes the same `set`
  input as `calibrate.yml` (`both`, `urgency` or `banking77`, default `both`). It bills nothing.

Each run calls `minos calibrate --config docs/reports/minos.json --provider <TypeSafe|Clef|Local>` with
`--parallel 1`, so the items are sent one at a time.

## Applying prices

A run does not apply prices. They are applied afterwards, without calling a provider:

```text
minos report --results <results> --dataset <dataset> \
  --prices docs/reports/prices.json --out docs/reports/<provider>/<set>
```

This rebuilds `report.json` and `report.md` from the downloaded results. A price correction therefore never needs a new
run, and the folder's README says that the report was rebuilt with prices.

## Refreshing the reports

After a change to the report writer, `EveryReport_RebuildsFromItsResults` fails until each report is rebuilt with
`minos report` from the current tool. The folder's README then records, on its `Tool:` line, which tool version rebuilt
it.

## The shadow report

A shadow report is a different kind of run from a calibration. It does not score a provider against labels in one go. It
replays traffic through [shadow mode](../recording.md#shadow-mode): the primary decides each item, and the
candidate decides the same item in the background. The report then compares the two. It is evidence only: it states what
was measured and gives no verdict on whether to switch.

The first set is `banking77`. Its folder is [`docs/reports/shadow/banking77/`](shadow/banking77/README.md), from the
approved run of 2026-11-01.
Each set has its own folder, `docs/reports/shadow/<set>/`, with four files:

- `README.md` says what the run was, in `Field: value` lines as in the calibration folders: Date, Primary, Candidate, Dataset,
  Run, Runner, Served, Tool and Prices. A new shadow folder adds its run to `ShadowRunIds` in `ReportFolderTests`, which
  also rebuilds each shadow report from its records and the prices.
- `records.jsonl` holds the records of the replay: two record lines and one outcome line for each decided item. A primary
  call that failed writes one line.
- `shadow-report.json` is the report in machine-readable form.
- `shadow-report.md` is the same report to read in the repository.

### How the run is started

The run is started by hand, like every other run here:

```text
gh workflow run local-systemone.yml --ref main -f job=shadow
```

The `shadow` job is dispatch only. It is billed, because the primary is TypeSafe and the replay makes about 400 TypeSafe
calls, so it is started only after the maintainer approves the run. It uses the `live-api` environment, which deploys
only from `main`, so the `--ref` must be `main`.

The job replays `datasets/banking77/banking77-minos-v1.jsonl` one item at a time through
[`samples/Minos.NET.Samples.ShadowReplay`](../../samples/Minos.NET.Samples.ShadowReplay/README.md). TypeSafe is the primary
and the local `decider-2b` server is the candidate. The confirmed outcome of each item comes from the dataset label, with
the source `banking77-label`. The job then runs `minos shadow-report` from Minos.NET.Tool 1.8.0 with
`docs/reports/prices.json`, and uploads the artifact `shadow-typesafe-local-banking77`, which holds `records.jsonl`,
`shadow-report.json` and `shadow-report.md`.

The traffic is replayed, not live. The items are the dataset's, sent in a loop, and no production request is shadowed.

## The triage report

A triage report measures the [ticket-triage sample](../../samples/Minos.NET.Samples.TicketTriage/README.md), where a local
model decides each ticket and a hosted model is asked only when the local one is unsure. It is evidence only: it states
what was measured and gives no verdict. The report is built from the records of the run, so it can be rebuilt without a
provider call.

The first set is `banking77`. Its folder is [`docs/reports/triage/banking77/`](triage/banking77/README.md), from the
approved run of 2026-11-06. Each set has its own folder, `docs/reports/triage/<set>/`, with the files the run
produced: `records.jsonl`, `triage-report.json` and `triage-report.md`, and a `README.md` that says what the run was.

### How the run is started

The run is started by hand, like every other run here:

```text
gh workflow run local-systemone.yml --ref main -f job=triage
```

The time one ticket has for the local call and the escalation is 5 seconds by default. A CPU runner can need more than
that for one local call, and when it does, every unsure question is kept on the local answer for lack of time and the
target is not called. The `triage_budget_seconds` input sets the budget, as a whole number of seconds from 1 to 300, for
the `triage` job only:

```text
gh workflow run local-systemone.yml --ref main -f job=triage -f triage_budget_seconds=15
```

The run does not print the budget, so the `README.md` in the set's folder records the value that was used.

The `triage` job is dispatch only. It is billed, because TypeSafe answers every ticket the local model is unsure about,
so it is started only after the maintainer approves each run. It uses the `live-api` environment, which deploys only from
`main`, so the `--ref` must be `main`.

The job runs the sample over `datasets/banking77/banking77-minos-v1.jsonl` one item at a time. The thresholds are fixed
in the sample, not read from `docs/reports/minos.json`; the
[settings table](../../samples/Minos.NET.Samples.TicketTriage/README.md#what-it-does) of the sample lists them. A
`Triage report` step then builds the report from the records with `docs/reports/prices.json`,
and the job uploads the artifact `triage-local-typesafe-banking77`, which holds `records.jsonl`, `triage-report.json` and
`triage-report.md`. The report step runs even when the run stops early, so a partial run still gets a report.

The job has no price guard. The model that TypeSafe served is known only after a call, so the price check is a manual
step before the run: confirm that `docs/reports/prices.json` has a price for the model that `jev-latest` resolves to. If a
run served a model with no price, add the price to `docs/reports/prices.json` and rebuild the report from the committed
records:

```text
dotnet run --project samples/Minos.NET.Samples.TicketTriage -c Release -- --report --records docs/reports/triage/banking77/records.jsonl --dataset datasets/banking77/banking77-minos-v1.jsonl --prices docs/reports/prices.json --out docs/reports/triage/banking77
```
