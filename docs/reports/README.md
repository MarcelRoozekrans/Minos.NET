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
- `prices.json` holds the prices applied to a report afterwards: the TypeSafe model `jev-1.13.0`, and the local model at
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

The first set is `banking77`. Its folder is `docs/reports/shadow/banking77/`, and it is committed after an approved run.
Each set has its own folder, `docs/reports/shadow/<set>/`, with four files:

- `README.md` says what the run was: the set, the primary, the candidate, the date and the tool version.
- `records.jsonl` holds the recorded decisions of the replay, one line for each item.
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
