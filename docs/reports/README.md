# Calibration reports

This folder holds the calibration runs of Minos and the files that produce them. It is not part of the docs site: the
site excludes `docs/reports/`, and the tool-written `report.md` files in it are read in the repository.

The six reports of the first calibration are committed, one folder for each provider and set.

## Layout

Each run has its own folder, `docs/reports/<provider>/<set>/`:

- `<provider>` is `typesafe`, `clef-flash` or `local`.
- `<set>` is `banking77` (`datasets/banking77/banking77-minos-v1.jsonl`) or `urgency`
  (`datasets/banking77/banking77-urgency-v1.jsonl`).

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
