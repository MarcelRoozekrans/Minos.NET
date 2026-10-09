# Pre-push review: phase/6.1-rename

| Metric | Value |
|---|---|
| Date | 2026-10-09 21:14 |
| Branch | phase/6.1-rename |
| Base Branch | main (merge base dbd120a) |
| Commit range | dbd120a..9f7779a |
| Commits Reviewed | 26 |
| Files Changed | 588 |
| Lines Added | 32145 |
| Lines Removed | 6240 |
| Verdict | **PASS** |

Findings: 0 blockers, 0 warnings, 3 info.

## Plan adherence

The plan is `docs/superpowers/plans/2026-10-09-phase-6.1-rename-to-minos.md`, with the spec `docs/superpowers/specs/2026-10-09-phase-6.1-rename-design.md` and the impact analysis `docs/plans/2026-10-09-phase-6.1-rename-impact-analysis.md`.

Tasks implemented in this branch: 12 of 12. Task 13 happens outside the repository after the merge.

| Task | Commits |
|---|---|
| 1 Rename tool | 589dd1f, 962824a, dceb550, 5ace640, 97783c3 |
| 2 Projects, assemblies, namespaces | ceff588, cbdb2e0, 5514419 |
| 3 Type renames | 5bc6a12, e5354ab |
| 4 JEV → MIN | bfd78cf, cd41275 |
| 5 Prose pass | 46c903a |
| 6 Logo | 47d68d5 |
| 7 Metadata, README, pack tests | 47d68d5 |
| 8 CI independence | 1049c9c |
| 9 Docs site | e935f2c |
| 10 Guard | cf6018b, 0950d3a, 235aa7d, 9f7779a |
| 11 Planning state | 9c1f695 |
| 12 Verification | see Regression tests |
| 13 Maintainer actions | after merge; checklist in `docs/planning/STATE.md` |

Unplanned changes:

- **Info:** cbdb2e0 gives the mid-request disposal test its own WireMock server. Reordering the tests during the move exposed this flaky test. The fix is test-only.
- **Info:** the package tags differ from the spec's list: `minos` was added, `ai` was dropped, and `system-one` became `systemone`. The PR body states this.

## Code quality

- **Security:** no findings. The diff has no new network endpoints, deserialisation paths or credential handling.
- **Debug and temporary code:** none in `*.cs`. The three `#pragma warning disable` lines in the diff all come from moved files and already exist on main:
  - `MIN005` in `Docs.Tests/Diagnostics.cs` was renamed from `JEV005`;
  - two `HLQ005` lines are in the analyzer and generator tests.
- **YAGNI and dead code:** `tools/rename` is a one-off migration tool. It is kept until the merge so reviewers can re-run it. Its deletion is tracked in the STATE.md maintainer checklist.
- **Naming:** the naming rule is enforced by tests:
  - NoOldNameTests scans every tracked text file;
  - the analyzer release-tracking check ties each rule's category to its id prefix;
  - NameClashTests covers the Question and Answer aliases.
- **Test coverage:** the pack, guard and name-clash tests are new, and the rename tool has its own unit tests.
- **Per-task reviews:** each task passed its own reviews, and the final whole-branch review and its fix wave came back clean.

## Commit hygiene

- **Messages:** all 26 commits are conventional, and their scopes come from `.commitlintrc.yml`.
  - No body contains nested parentheses, so release-please can parse every commit.
  - No body contains a session link, and every commit has a Co-Authored-By trailer.
- **Secrets:** a scan for API-key, token and private-key patterns in the added lines found nothing.
- **Conflict markers:** none.
- **Unintended files:** no `bin/`, `obj/`, `node_modules/`, `.env` or OS files.
- **Large files:**
  - **Info:** `package-lock.json` is 747 KB. It is the Docusaurus site's lockfile, and it is needed for reproducible `npm ci` in docs.yml.
  - The brand rasters in `assets/brand/` and `static/img/favicon.ico` are small PNG and ICO files.
  - `assets/icon.png` was deleted and replaced by `assets/brand/icon-128.png`.

## Regression tests

The controller ran the Task 12 verification on 9f7779a's tree. Nothing in the tree changed after that.

- **Build:** `dotnet build` with TreatWarningsAsErrors gave 0 warnings and 0 errors.
- **Tests (`dotnet test`):** 0 failures.

  | Suite | Result |
  |---|---|
  | core | 840 passed |
  | Docs | 369 passed |
  | Analyzers | 176 passed |
  | Samples | 139 passed |
  | Generator | 131 passed |
  | Benchmarks | 129 passed |
  | DI | 42 passed |
  | AotSmoke | 31 passed |
  | Integration | 27 passed |
  | Pack | 23 passed |
  | Live | 13 skipped (opt-in, by design) |

- **Other checks:**
  - `dotnet mdsnippets`: no diff.
  - The `tools/rename` unit tests pass.
  - All 21 allocation budgets in AllocationChecks.cs are identical on main and on the branch.

There is no web UI to test, and no UI contract exists.
