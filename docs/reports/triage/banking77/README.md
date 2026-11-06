# Triage report: local model with TypeSafe as the target, banking77

Date: 2026-11-06
Primary: custom, Mapika/decider-2b-GGUF:Q4_K_M
Target: typesafe, jev-1.14.0
Thresholds: intent (Choice) 0.85, card_not_arrived 0.80, about_a_payment 0.80
Budget: 15 seconds for each ticket, set with `triage_budget_seconds=15`
Expected latency: 300 ms
Dataset: banking77-minos-v1
Run: https://github.com/MarcelRoozekrans/Minos.NET/actions/runs/42239475037
Runner: GitHub-hosted ubuntu-latest, CPU only, with the primary served locally by ollajev 0.4.0 from the file Q4_K_M of Mapika/decider-2b-GGUF
Served: primary Mapika/decider-2b-GGUF:Q4_K_M; target jev-1.14.0
Tool: Minos.NET.Samples.TicketTriage at commit 240353f6, dispatched from main
Prices: jev-1.14.0 at 0.042 input and 0 output per million tokens, and the local model at 0, from docs/reports/prices.json; applied by the report step of the run

The traffic was replayed from the Banking77 set, not taken from production. The `triage` job of `local-systemone.yml`
sent the 400 items of `datasets/banking77/banking77-minos-v1.jsonl` one at a time through
`samples/Minos.NET.Samples.TicketTriage`, the pipeline an app would build: the local model answers the three questions
of each ticket, and TypeSafe is asked only the questions whose local answer is below its threshold. The report gives
figures and no verdict on whether to adopt escalation.

`records.jsonl` is the run's own output. It holds the routing and the answers of each ticket and no message text.
`triage-report.json` and `triage-report.md` were rebuilt from it with the sample's `--report` mode, the dataset and
`docs/reports/prices.json`, without a call to either provider. They are identical to the files the run uploaded.

An earlier run of the same sample with the default budget of 5 seconds, Actions run 42230673543 on the same day, made no
target call. The local call took a median of 5.5 s and p95 6.1 s on the runner, so all 106 questions below their
threshold ended as no time left and nothing was billed. Its records are not committed. This run used a budget of 15
seconds for that reason.

The latency of a call includes its retries. The budget leaves out the time the host spends preparing a ticket, so it
bounds the local call and the escalation only.
