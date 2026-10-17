# Calibration report: Clef Flash, banking77

Date: 2026-10-17
Provider: openrouter
Model: cloudflare/clef-flash
Dataset: banking77-minos-v1
Run: https://github.com/MarcelRoozekrans/Minos.NET/actions/runs/39053417194
Runner: GitHub-hosted ubuntu-latest, calling OpenRouter with zero data retention requested
Served: cloudflare/clef-flash
Tool: Minos.NET.Tool 1.2.0
Prices: reported by the provider, per response; docs/reports/prices.json has no entry for this model

The run was `minos calibrate` with `--parallel 1` and the settings in `docs/reports/minos.json`, started by hand with
`workflow_dispatch`. Every one of its 400 requests succeeded.

`results.jsonl` is the run's own output. `report.json` and `report.md` were rebuilt from it with `minos report --results
results.jsonl --dataset <dataset> --prices docs/reports/prices.json --out .`, so the prices are applied without a new
call to the provider.
