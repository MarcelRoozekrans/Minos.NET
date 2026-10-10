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
**Released by:** release-please (configured: `release-please.yml`, no publish job)
**Milestone completion tags a release:** no
**Changelog:** auto

## Deployment

**Deploy target:** nuget.org (package id ZeroAlloc.Jev, publishing off until the maintainer declares it mature, #29); the org website at jev.zeroalloc.net via ZeroAlloc-Net/.website (docs)
**Environments:** live-api (manual live smoke; no deployment environment)
**Deployed by:** GitHub Actions: release-please opens release PRs and creates GitHub releases; `trigger-website.yml` dispatches doc updates to the org website; NuGet publishing is not configured (#29)
