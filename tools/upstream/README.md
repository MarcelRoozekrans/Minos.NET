# Upstream decision abstractions

Minos.NET maps its decision model onto the experimental decision abstractions
in `dotnet/extensions` (`IDecisionClient` and related types in
`Microsoft.Extensions.AI.Abstractions`). Those types are not yet in a released
NuGet package, so this folder pins the upstream commit that holds them.

## The pin

`decisions-pin.json` holds:

- `repository`: the GitHub repository, `dotnet/extensions`.
- `commit`: the full SHA the abstractions are built from.
- `checked`: the date the pin was last confirmed to contain the types.
- `project`: the path of the Abstractions csproj inside that repository.

## Building

```bash
tools/upstream/build-decisions.sh
```

Needs `bash`, `jq`, `git` and a .NET SDK. The script shallow-fetches the pinned
commit into `artifacts/upstream/src`, builds the one project in Release, and
writes the full path of the net10.0 `Microsoft.Extensions.AI.Abstractions.dll`
to `artifacts/upstream/abstractions.path`. Re-running with the same pin reuses
the checkout; a changed pin refetches.

The build is local only. Everything lands under `artifacts/`, which git
ignores, and the DLL is never packed or shipped with a Minos.NET package.

### Why the build works without extra setup

The upstream `global.json` pins SDK 10.0.401 and Arcade SDKs. Building the csproj
directly lets Arcade restore itself, and a newer installed 10.0.4xx patch
satisfies the SDK pin through the default roll-forward. No repo-specific
property is required. The script passes two properties anyway:
`-p:TreatWarningsAsErrors=false` so upstream analyzer warnings from a newer SDK
cannot fail the build, and `-p:ContinuousIntegrationBuild=false` so the build is
not forced into deterministic CI mode on CI runners.

## Bumping the pin

1. Pick the new commit in `dotnet/extensions`.
2. Update `commit` and `checked` in `decisions-pin.json`, and `project` if the
   csproj moved.
3. Run `tools/upstream/build-decisions.sh` and confirm it succeeds.
4. Re-run the mapping tests, then update the docs mapping for any renamed or
   changed type.
