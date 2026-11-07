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

### SDK and properties

Upstream `global.json` pins SDK 10.0.401, but the build uses this repository's
`global.json` (10.0.403, `rollForward` `latestMinor`), because the SDK is chosen
from the current directory and the script changes to the repo root before it
builds, wherever it is called from. CI installs the SDK from the repo's
`global.json`. The script passes
`-p:TreatWarningsAsErrors=false` so upstream analyzer warnings from a newer SDK
cannot fail the build, and `-p:ContinuousIntegrationBuild=false` to keep the
build out of deterministic CI mode. Neither was needed locally.

## Bumping the pin

1. Pick the new commit in `dotnet/extensions`.
2. Update `commit` and `checked` in `decisions-pin.json`, and `project` if the
   csproj moved.
3. Run `tools/upstream/build-decisions.sh` and confirm it succeeds.
4. Re-run the mapping tests, then update the docs mapping for any renamed or
   changed type.

The pinned commit may be a PR head, which can disappear upstream if the branch
is force-pushed or deleted; the fetch then fails. Prefer a merged or tagged
commit as soon as one exists.

## Finding a released package

```bash
tools/upstream/find-decisions-package.sh
```

Looks on nuget.org for a `Microsoft.Extensions.AI.Abstractions` version newer
than 10.11.0, preview included, whose assemblies export the public type
`Microsoft.Extensions.AI.IDecisionClient`. It prints the first such version and
exits 0, prints nothing and exits 1 when there is none, and exits 2 on a network
or tool error. The type check is `HasType.cs`, a file-based app that uses
`MetadataLoadContext`. `test-find-decisions-package.sh` tests the check against
local assemblies and needs the pinned build first.
