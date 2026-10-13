# Releasing Minos.NET

Releases are cut by release-please and published to nuget.org by `ci.yml`. Nothing is published by hand.

## How a release happens

1. Conventional commits on `main` make release-please keep a release PR open, named `chore(main): release X.Y.Z`.
2. Merging that PR tags `vX.Y.Z` and creates the GitHub release.
3. `release-please.yml` then dispatches `ci.yml` on the tag with `publish_to_nuget=true`.
4. On that run, every job runs on the tagged commit. `publish-nuget` starts only after `build`, `aot-smoke`, `aot-surface` and `pack-validate` pass.
5. `publish-nuget` refuses a 0.x version, a prerelease, or a tag that is not the manifest's version. It then packs with `-p:MinosRelease=true`, inspects the packages, pushes the six `.nupkg` files to nuget.org, and attaches them and their `.snupkg` files to the GitHub release.

Every push and pull request also runs `pack-validate`. It packs the same way, inspects the packages, and rehearses the push against a local feed.

## Before the first release: two settings on nuget.org and GitHub

- **Trusted Publishing:** on nuget.org, add a Trusted Publishing policy for the repository `MarcelRoozekrans/Minos.NET` and the workflow file `ci.yml`.
- **`NUGET_USER`:** in the GitHub repository, add a variable named `NUGET_USER` holding the nuget.org user name that owns the policy.

Without both, `publish-nuget` fails at its login or push step and nothing reaches nuget.org.

## Re-running a publish

A push that died partway can be finished by dispatching the same run again on the tag:

    gh workflow run ci.yml --ref vX.Y.Z -f publish_to_nuget=true

`--skip-duplicate` skips the packages that already arrived. Dispatching on `main` publishes nothing, because the job runs only on a `v*` tag.

## The version

The version is the `"."` entry of `.release-please-manifest.json`. A build without `-p:MinosRelease=true` is versioned `X.Y.Z-local`, and that version is compiled into the DLLs. So every release pack builds with the switch, and the inspection rejects a DLL that does not carry the release version.
