# Conformance: typesafe

- Mode: Replay
- Wire: SystemOne
- Model: `jev-latest`
- Model rule: The reported model is the requested one or, for an alias such as jev-latest, a versioned id of the same family, such as jev-1.13.0.
- Fixture: tests
- Result: 1 passed, 1 failed, 3 skipped

| Probe | Check | Outcome | Detail |
| --- | --- | --- | --- |
| noul | S1 | pass | One answer for each question asked, 1 in all. |
| noul | D2 | skipped | The probe asks no Choice or Score question. |
| noul | R2 | fail | gpt-6-luna does not fit jev-latest. A \| in a detail is escaped. |
| invalid | E1 | skipped | The fixture skips it: no body yet. |
| run | E4 | skipped | No 429 was met during the run. |
