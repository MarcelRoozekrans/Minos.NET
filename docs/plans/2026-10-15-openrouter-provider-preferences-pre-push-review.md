# Pre-push review — OpenRouter provider preferences, #202

| Metric | Value |
|---|---|
| Date | 2026-10-15 |
| Branch | `feat/openrouter-provider-preferences` |
| Base Branch | `origin/main` (b3697aa), rebased |
| Commits Reviewed | 8 |
| Files Changed | 25 |
| Lines Added / Removed | about 1,080 / 27 |
| Verdict | **PASS** |

## Plan adherence

The plan is `docs/superpowers/plans/2026-10-15-openrouter-provider-preferences.md`, and the spec is `docs/superpowers/specs/2026-10-15-openrouter-provider-preferences-design.md`.

- Tasks 1 to 4 are fully implemented.
- Task 5's code, the live tests, is in. Its remaining steps happen after the merge, by design:
  - the live run on `main`;
  - the docs line;
  - closing #202.
- Deviations, all recorded as rulings in the execution ledger:
  - The writer appends the provider bytes after the flush.
  - The AOT gate is left to CI: no linker locally, and the IL compiler reported no trim warnings.
  - The raw path copies the request when preferences are on.
  - The AOT smoke app gets a functional check, not a new budget.
  - The docs JSON block indices move up by one.
  - The live tests are a separate file with a control request.
  - **The docs example sets `ZeroDataRetention` alone** until the live run settles `DenyDataCollection`.

## Code quality

A whole-branch review by a fresh reviewer found 0 Critical, 0 Important and 4 Minor issues. All four are fixed in `fix(systemone): keep provider off a caller's own serialization`:

1. A caller's own serialization wrote `"provider":null`. Fixed: `JsonIgnore WhenWritingNull` on the member, with a test that uses caller-reachable options.
2. The docs example set the unverified `DenyDataCollection`. Fixed, and the docs line now says it is filled in before the release.
3. The image body test was weak. It now compares the whole body.
4. The live check skipped the typed path. It now makes a typed call too.

Security: no secrets. The API key in the live test comes from the environment.

## Commit hygiene

- Conventional messages, with scopes from `.commitlintrc.yml`.
- No nested parentheses in commit bodies.
- No secrets, no conflict markers, no generated or large files.

## Regression tests

After the rebase onto `origin/main`:
- `dotnet build Minos.NET.slnx -c Release`: 0 warnings, 0 errors.
- `dotnet test Minos.NET.slnx -c Release`: 14 projects passed. The live project skipped without keys: 23 tests, including the 3 new ones.

Not run locally:
- **The Native AOT publish.** No clang or gcc here; CI's required `aot-smoke` and `aot-surface` jobs run it. The new functional smoke check passed on the JIT.

## Findings

0 blockers, 0 warnings. The live verification is still open; it runs after the merge, and the 1.1.0 release PR waits for it.
