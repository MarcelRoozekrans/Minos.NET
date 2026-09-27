# Project Conventions

> Written by `init-conventions`. Do not hand-edit — re-run the sub-skill instead; the Commit & Release Protocol reads these fields.

**Established:** 2026-09-24

## Stack

**Language / runtime:** C#, .NET 10 (net10.0 only — ZeroAlloc.Rest requires it)
**Package manager:** NuGet (dotnet CLI)
**Framework:** ZeroAlloc.* (Rest, Resilience, Results, Telemetry, Validation, Inject)
**Datastore:** n/a

## Commits

**Format:** conventional (decided)
**Scopes:** enforced
**Scope source:** .commitlintrc.yml
**Fallback when scope not allowed:** omit scope

## Branching

**Model:** trunk
**PR required:** no (detected 2026-09-27: `main` on ZeroAlloc-Net/ZeroAlloc.Jev is unprotected)
**Protected branches:** none

## Versioning & Release

**Scheme:** semver (decided)
**Released by:** release-please (decided; to be configured)
**Milestone completion tags a release:** no
**Changelog:** auto

## Deployment

**Deploy target:** nuget.org (package id ZeroAlloc.Jev, publishing off until the maintainer declares it mature), GitHub Pages (docs site)
**Environments:** github-pages
**Deployed by:** GitHub Actions: release-please opens release PRs and creates GitHub releases; NuGet publishing and the Pages deploy are not configured yet (NuGet waits until the maintainer declares the package mature)
