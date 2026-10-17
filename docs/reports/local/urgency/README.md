# Calibration report: the local model, urgency

Date: 2026-10-17
Provider: custom
Model: Mapika/decider-2b-GGUF:Q4_K_M
Dataset: banking77-urgency-v1
Run: https://github.com/MarcelRoozekrans/Minos.NET/actions/runs/39053226413
Runner: GitHub-hosted ubuntu-latest, CPU only, with the model served locally by ollajev
Tool: Minos.NET.Tool 1.2.0
Prices: 0 per million tokens, from docs/reports/prices.json, because the local server reports no cost; applied after the run

The run was `minos calibrate` with `--parallel 1` and the settings in `docs/reports/minos.json`, started by hand with
`workflow_dispatch`. Every one of its 60 requests succeeded.

`results.jsonl` is the run's own output. `report.json` and `report.md` were rebuilt from it with `minos report --results
results.jsonl --dataset <dataset> --prices docs/reports/prices.json --out .`, so the prices are applied without a new
call to the provider.
