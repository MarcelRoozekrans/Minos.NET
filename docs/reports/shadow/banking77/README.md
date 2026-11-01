# Shadow report: TypeSafe against the local model, banking77

Date: 2026-11-01
Primary: typesafe, jev-1.14.0
Candidate: custom, Mapika/decider-2b-GGUF:Q4_K_M
Dataset: banking77-minos-v1
Run: https://github.com/MarcelRoozekrans/Minos.NET/actions/runs/41292378108
Runner: GitHub-hosted ubuntu-latest, CPU only, with the candidate served locally by ollajev 0.4.0 from the file Q4_K_M of Mapika/decider-2b-GGUF
Served: primary jev-1.14.0; candidate Mapika/decider-2b-GGUF:Q4_K_M
Tool: Minos.NET.Tool 1.8.0
Prices: jev-1.14.0 at 0.042 input and 0 output per million tokens, and the local model at 0, from docs/reports/prices.json; applied after the run

The traffic was replayed from the Banking77 set, not taken from production. The `shadow` job of `local-systemone.yml`
sent the 400 items of `datasets/banking77/banking77-minos-v1.jsonl` one at a time through
`samples/Minos.NET.Samples.ShadowReplay`: TypeSafe decided each item, and the local model decided the same item in shadow
mode. The confirmed outcome of each item is its dataset label, with the source `banking77-label`. The replay ended with
`items=400 decided=400 primary_failed=0 shadowed=400 waits_run_out=0`. The report gives figures and no verdict on
whether to switch.

`records.jsonl` is the run's own output. TypeSafe served `jev-1.14.0`, which `prices.json` did not price at the time of
the run, so the run's report had no cost for the primary. The price was added afterwards, and `shadow-report.json` and
`shadow-report.md` were rebuilt from the records with `minos shadow-report --records records.jsonl --prices
docs/reports/prices.json --out .`, without a new call to either provider.
