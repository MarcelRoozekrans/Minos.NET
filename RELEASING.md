# Releasing Minos.NET

Releases are cut by release-please and published to nuget.org by `ci.yml`. Nothing is published by hand.

## How a release happens

1. Conventional commits on `main` make release-please keep a release PR open, named `chore(main): release X.Y.Z`.
2. Merging that PR tags `vX.Y.Z` and creates the GitHub release.
3. `release-please.yml` then dispatches `ci.yml` on the tag with `publish_to_nuget=true`.
4. On that run, the jobs `publish-nuget` needs, `build`, `aot-smoke`, `aot-surface` and `pack-validate`, run on the tagged commit. `publish-nuget` starts only after they pass.
5. `publish-nuget` refuses a 0.x version, a prerelease, or a tag that is not the manifest's version. It then packs with `-p:MinosRelease=true`, inspects the packages, pushes the six `.nupkg` files to nuget.org, and attaches them and their `.snupkg` files to the GitHub release. nuget.org takes
each `.snupkg` with the same push as its `.nupkg`.

While the version is 0.x, the dispatched run's `publish-nuget` job fails on purpose, with the message "0.x versions are not published". That red run is expected and nothing is pushed.

Every push and pull request also runs `pack-validate`. It packs the same way, inspects the packages, and rehearses the push against a local feed.

## Before the first release: settings on nuget.org and GitHub

- **Trusted Publishing:** on nuget.org, add a Trusted Publishing policy for the repository `MarcelRoozekrans/Minos.NET` and the
  workflow file `ci.yml`. All six package ids are new on nuget.org, so the policy must be allowed to push new packages as well as
  new versions: the owner or policy setting that allows pushing new packages and package versions. Scope it to the six ids or to
  the glob `Minos.NET*`.
- **Reserve the id prefix:** also reserve the `Minos.NET` id prefix on nuget.org. Anyone can take an id until it is published.
- **`NUGET_USER`:** in the GitHub repository, add a variable named `NUGET_USER` holding the nuget.org user name that owns the policy.

Without both, `publish-nuget` fails at its login or push step and nothing reaches nuget.org.

## Re-running a publish

A push that died partway can be finished by dispatching the same run again on the tag:

    gh workflow run ci.yml --ref vX.Y.Z -f publish_to_nuget=true

`--skip-duplicate` skips the packages that already arrived. Dispatching on `main` publishes nothing, because the job runs only on a `v*` tag.

A re-run skips a package that already arrived, and may not re-send its symbol package. If a symbol package is missing on nuget.org
after a publish, it is attached to the GitHub release and can be uploaded on nuget.org's web upload page.

## The version

The version is the `"."` entry of `.release-please-manifest.json`. A build without `-p:MinosRelease=true` is versioned `X.Y.Z-local`, and that version is compiled into the DLLs. So every release pack builds with the switch, and the inspection rejects a DLL that does not carry the release version.
