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

**Model:** feature-branch
**PR required:** yes (ruleset "Main" on MarcelRoozekrans/Minos.NET, updated 2026-10-09; the branch-protection endpoint does not report rulesets)
**Protected branches:** main

## Versioning & Release

**Scheme:** semver (decided)
**Released by:** release-please (`release-please.yml`; merging a release PR tags it and dispatches `ci.yml`'s `publish-nuget`)
**Milestone completion tags a release:** no
**Changelog:** auto

## Deployment

**Deploy target:** nuget.org, nine `Minos.NET*` packages under the reserved `Minos.NET` prefix owned by MarcelRoozekrans; the docs site on GitHub Pages at marcelroozekrans.github.io/Minos.NET
**Environments:** live-api (manual live smoke; no deployment environment)
**Deployed by:** GitHub Actions: `ci.yml`'s `publish-nuget` through NuGet Trusted Publishing, gated to a `v*` tag and refusing 0.x and prereleases (`RELEASING.md`); `docs.yml` deploys the site
