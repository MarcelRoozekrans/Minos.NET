# Milestone 6 audit: Provider-neutral core

| Metric | Value |
|---|---|
| Date | 2026-10-12 |
| Commit | `main` at 05b2098, which is #169 |
| Milestone | 6, Provider-neutral core, started 2026-10-09 |
| Verdict | **FAIL**: 1 gap fixable now, 2 items blocked on an OpenAI key |

## Definition of done

| # | Criterion | Verdict | Evidence |
|---|---|---|---|
| 1 | All planned phases complete | Met | Phases 6.1 to 6.7 are `[status: complete]` in `ROADMAP.md`; each has a spec and a plan on disk. |
| 2 | All tests pass, including samples in replay mode and the AOT smoke and surface checks | Met | Local run on 05b2098: 0 warnings; 2,778 passed, 0 failed, 20 live skipped. CI run 38216065113 on 05b2098 passed, including `aot-smoke`, `aot-surface` and the samples step, which now replays three provider files. |
| 3 | Name decision recorded; no public type, namespace, package id or analyzer ID says "Jev"; repository and site moved | Met | The decision is in `STATE.md` and the Phase 6.1 spec. No `PublicAPI` file, namespace, package id or analyzer ID carries Jev; the analyzer IDs are `MIN`. "Jev" remains only in prose naming TypeSafe's API and in model ids such as `jev-latest`, which is the provider's own name. The repository is `MarcelRoozekrans/Minos.NET` and the site is live. One maintainer item is listed in `STATE.md`: removing the sonarqubecloud app. |
| 4 | No `/v1/systemone` wire JSON in the generator's output; serialization only in protocol adapters; **a test serializes one question set for both protocols** | **Partly met** | `ProtocolBoundaryTests` keeps wire field names out of the core, and the generator source has no wire JSON. No test serializes one question set through both the System One and the OpenAI protocol: `OpenAIRequestWriterTests` covers OpenAI alone and the System One tests cover System One alone. |
| 5 | `IDecisionClient` with retries, telemetry and logging as pipeline stages; every `/v1/systemone` allocation budget unchanged or tightened | Met | Phase 6.3 shipped the pipeline. The budgets in `samples/Minos.NET.AotSmoke/AllocationChecks.cs` at dbd120a, the last Milestone 5 commit, against 05b2098: no existing gate was raised; the gates for removed API went with it; new gates cover the neutral, OpenAI and image paths. `aot-smoke` passes in CI run 38216065113. |
| 6 | Both adapters have recorded-fixture tests; an unsupported kind or image state fails with a typed error naming provider and question | Met | System One has WireMock tests and recorded TypeSafe and OpenRouter conformance fixtures. OpenAI has fixture tests in `tests/Minos.NET.OpenAI.Tests/Fixtures`, built from its guide, plus a real 401. The typed errors are pinned by `CapabilityTests`, `OpenAICheckTests`, `ProvidersTests.Images_ToAProviderWithoutThem_AreMin206_AndNothingIsSent` and the integration test that refuses a Choice set unsent. |
| 7 | Live runs pass against TypeSafe, OpenRouter, OpenAI and one local `/v1/systemone` server | **Partly met, blocked** | Live smoke run 38214563649 passed against TypeSafe and OpenRouter; Local systemone run 38216065098 passed against ollajev with decider-2b. No OpenAI run exists: there is no key. #161 tracks it. |
| 8 | The conformance suite runs green against recorded TypeSafe and OpenAI fixtures | **Partly met, blocked** | Green on recorded TypeSafe and OpenRouter fixtures. OpenAI's fixture is labelled `guide`, because there is no key to record with. #161 tracks it. |
| 9 | A Providers page with the presets and the capability matrix; a sample switches providers by configuration only | Met | `docs/providers.md` opens with the matrix, tested against each client's resolved capabilities. `samples/Minos.NET.Samples.ProviderSwitch` replays TypeSafe, OpenRouter and a local server; `TheProviderSections_DifferOnlyInProviderSettings` pins that only provider settings differ. |

## Code quality reviews

Every phase has a pre-push review with a PASS verdict in `docs/plans/`:

| Phase | Report | Verdict |
|---|---|---|
| 6.1 | `2026-10-09-phase-6.1-pre-push-review.md` | PASS |
| 6.2 | `2026-10-10-phase-6.2-pre-push-review.md` | PASS |
| 6.3 | `2026-10-10-phase-6.3-pre-push-review.md` | PASS |
| 6.4 | `2026-10-11-phase-6.4-pre-push-review.md` | PASS, 1 warning |
| 6.5 | `2026-10-11-phase-6.5-pre-push-review.md` | PASS, 1 warning |
| 6.6 | `2026-10-11-phase-6.6-pre-push-review.md` | PASS |
| 6.7 | `2026-10-12-phase-6.7-pre-push-review.md` | PASS |

## Release

`CONVENTIONS.md` says `Milestone completion tags a release: no`; release-please owns releases, so this criterion is skipped. Releases v0.6.0 to v0.11.1 shipped during the milestone, and #168 for 0.11.2 is open. Nothing is published to NuGet in this milestone, as its goal says.

## Gaps

1. **No test serializes one question set for both protocols** (criterion 4). Fixable now: one test that sends the same generated set through a `SystemOneClient` and an `OpenAIDecisionClient`, captures both request bodies, and pins each against the wire format it expects.
2. **No live OpenAI run** (criterion 7). Blocked: there is no OpenAI key. Tracked in #161.
3. **OpenAI's conformance fixtures are not recorded** (criterion 8). Blocked on the same key. Tracked in #161.

Gaps 2 and 3 cannot be closed inside this milestone without a key. Milestone 7's definition of done already requires live runs against OpenAI, so they can move there, recorded as a decision, or the milestone stays open until a key exists. That is the maintainer's call.
