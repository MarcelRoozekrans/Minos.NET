# Calibration report: TypeSafe, banking77

Date: 2026-10-17
Provider: typesafe
Model: jev-latest
Dataset: banking77-minos-v1
Run: https://github.com/MarcelRoozekrans/Minos.NET/actions/runs/39053418745
Runner: GitHub-hosted ubuntu-latest
Tool: Minos.NET.Tool 1.2.0
Prices: jev-1.13.0 priced at 0.042 USD per million input tokens and 0 per million output tokens, from docs/reports/prices.json; applied after the run

The run was `minos calibrate` with `--parallel 1` and the settings in `docs/reports/minos.json`, started by hand with
`workflow_dispatch`. Every one of its 400 requests succeeded.

`results.jsonl` is the run's own output. `report.json` and `report.md` were rebuilt from it with `minos report --results
results.jsonl --dataset <dataset> --prices docs/reports/prices.json --out .`, so the prices are applied without a new
call to the provider.
