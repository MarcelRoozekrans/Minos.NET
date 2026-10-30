"""Checks the packages a release is about to push to nuget.org, before anything is pushed.

Usage: python3 tools/release/inspect_packages.py <directory> <version>

A NuGet version can be unlisted but never deleted, so ci.yml runs this in pack-validate on every push and again in
publish-nuget just before the push. It fails when the directory does not hold exactly the ten packages at the release
version, each with its .snupkg symbol package; when a package's DLL was built without -p:MinosRelease=true, which
Directory.Build.props versions <release>-local and which is compiled into the DLL; or when a package depends on another
Minos package at a different version. Minos.NET.Tool is a dotnet tool: it must have the DotnetTool package type and no
Minos dependencies, because it bundles its Minos assemblies in tools/net10.0/any, and each of those must carry the
release version. Standard library only.
"""
import json
import pathlib
import re
import sys
import zipfile

# Each package and the Minos packages it must depend on, from the ProjectReferences in its csproj.
PACKAGES: dict[str, tuple[str, ...]] = {
    "Minos.NET": (),
    "Minos.NET.DependencyInjection": ("Minos.NET",),
    "Minos.NET.SystemOne": ("Minos.NET", "Minos.NET.DependencyInjection"),
    "Minos.NET.OpenAI": ("Minos.NET", "Minos.NET.DependencyInjection"),
    "Minos.NET.Caching": ("Minos.NET",),
    "Minos.NET.RateLimiting": ("Minos.NET",),
    "Minos.NET.Evaluation": ("Minos.NET", "Minos.NET.Recording"),
    "Minos.NET.Recording": ("Minos.NET",),
    "Minos.NET.Extensions.AI": ("Minos.NET", "Minos.NET.DependencyInjection"),
    # A dotnet tool bundles everything it uses, so it has no Minos dependencies.
    "Minos.NET.Tool": (),
}

TOOL = "Minos.NET.Tool"
TOOL_DIRECTORY = "tools/net10.0/any/"


def _informational_version_found(dll: bytes, version: str) -> bool:
    # AssemblyInformationalVersion is stored as a length-prefixed UTF-8 string followed by 00 00: the release version,
    # optionally followed by +<commit>. The four-part FileVersion, 1.0.0.0, never matches, because a '.' follows the
    # version there. The byte before the string must be its length, which rejects a longer version that merely ends in
    # this one, such as 11.0.0 for 1.0.0.
    pattern = re.escape(version.encode("utf-8")) + rb"(?:\+[0-9A-Za-z.]+)?(?=\x00\x00)"
    return any(
        match.start() > 0 and dll[match.start() - 1] == len(match.group())
        for match in re.finditer(pattern, dll)
    )


def _symbol_problems(directory: pathlib.Path, package: str, version: str) -> list[str]:
    path = directory / f"{package}.{version}.snupkg"
    if not path.is_file():
        return [f"{package}: {path.name} is missing"]
    try:
        with zipfile.ZipFile(path) as archive:
            if not any(name.endswith(f"{package}.pdb") for name in archive.namelist()):
                return [f"{package}: {path.name} has no {package}.pdb"]
    except zipfile.BadZipFile as exc:
        return [f"{package}: {path.name} is not a valid symbol package: {exc}"]
    return []


def _runtime_asset_problems(package: str, deps: bytes | None, packaged: set[str]) -> list[str]:
    # The tool starts from its deps.json, which lists every assembly it loads. A listed assembly that is not in the
    # package fails only when that code path first runs, so --help alone does not show it.
    if deps is None:
        return [f"{package}: {TOOL_DIRECTORY}{package}.deps.json is missing"]
    try:
        targets = json.loads(deps)["targets"]
    except (ValueError, KeyError) as exc:
        return [f"{package}: {package}.deps.json cannot be read: {exc}"]
    missing: set[str] = set()
    for libraries in targets.values():
        for library in libraries.values():
            for asset in library.get("runtime", {}):
                if f"{TOOL_DIRECTORY}{asset.rsplit('/', 1)[-1]}" not in packaged:
                    missing.add(asset)
    return [f"{package}: {package}.deps.json lists {asset}, which is not in {TOOL_DIRECTORY}" for asset in sorted(missing)]


def _package_problems(directory: pathlib.Path, package: str, version: str) -> list[str]:
    errors: list[str] = []
    path = directory / f"{package}.{version}.nupkg"
    is_tool = package == TOOL
    dll_name = f"{TOOL_DIRECTORY}{package}.dll" if is_tool else f"lib/net10.0/{package}.dll"
    bundled: dict[str, bytes] = {}
    packaged: set[str] = set()
    deps: bytes | None = None
    try:
        with zipfile.ZipFile(path) as archive:
            nuspec = archive.read(f"{package}.nuspec").decode("utf-8")
            dll = archive.read(dll_name) if dll_name in archive.namelist() else None
            if is_tool:
                packaged = set(archive.namelist())
                deps_name = f"{TOOL_DIRECTORY}{package}.deps.json"
                deps = archive.read(deps_name) if deps_name in packaged else None
                for name in archive.namelist():
                    if name.startswith(f"{TOOL_DIRECTORY}Minos.NET") and name.endswith(".dll"):
                        bundled[name] = archive.read(name)
    except zipfile.BadZipFile as exc:
        return [f"{package}: {path.name} is not a valid package: {exc}"]
    except KeyError:
        return [f"{package}: {path.name} has no {package}.nuspec"]

    declared_id = re.search(r"<id>([^<]+)</id>", nuspec)
    if declared_id is None or declared_id.group(1) != package:
        errors.append(f"{package}: nuspec id is {declared_id.group(1) if declared_id else 'missing'}, expected {package}")

    declared = re.search(r"<version>([^<]+)</version>", nuspec)
    if declared is None or declared.group(1) != version:
        errors.append(f"{package}: nuspec version is {declared.group(1) if declared else 'missing'}, expected {version}")

    if dll is None:
        errors.append(f"{package}: {dll_name} is missing")
    elif not _informational_version_found(dll, version):
        errors.append(f"{package}: {package}.dll has no informational version {version}; was it built with -p:MinosRelease=true?")

    if is_tool:
        types = re.findall(r'<packageType\b[^>]*\bname="([^"]+)"', nuspec)
        if types != ["DotnetTool"]:
            errors.append(f"{package}: package type is {', '.join(types) or 'missing'}, expected DotnetTool")
        errors.extend(_runtime_asset_problems(package, deps, packaged))
        for name, content in sorted(bundled.items()):
            if name != dll_name and not _informational_version_found(content, version):
                errors.append(f"{package}: bundled {name.removeprefix(TOOL_DIRECTORY)} has no informational version {version}; was it built with -p:MinosRelease=true?")

    errors.extend(_symbol_problems(directory, package, version))

    accepted = {version, f"[{version}, )", f"[{version},)"}
    found: dict[str, list[str]] = {}
    for tag in re.findall(r"<dependency\b[^>]*>", nuspec):
        identity = re.search(r'\bid="([^"]+)"', tag)
        if identity is None or not identity.group(1).startswith("Minos.NET"):
            continue
        dependency_version = re.search(r'\bversion="([^"]+)"', tag)
        found.setdefault(identity.group(1), []).append(dependency_version.group(1) if dependency_version else "(no version)")
    for dependency in PACKAGES[package]:
        if dependency not in found:
            errors.append(f"{package} does not depend on {dependency}, expected {version}")
    for dependency, versions in found.items():
        if dependency not in PACKAGES[package]:
            errors.append(f"{package} depends on {dependency}, which is not one of its expected Minos dependencies")
        for dependency_version in versions:
            if dependency_version not in accepted:
                errors.append(f"{package} depends on {dependency} {dependency_version}, expected {version}")
    return errors


def problems(directory: pathlib.Path, version: str) -> list[str]:
    found = sorted(path.name for path in directory.glob("*.nupkg"))
    expected = sorted(f"{package}.{version}.nupkg" for package in PACKAGES)
    if found != expected:
        missing = sorted(set(expected) - set(found))
        extra = sorted(set(found) - set(expected))
        return [f"expected exactly the ten release packages in {directory}; missing {missing}, unexpected {extra}"]

    errors: list[str] = []
    for package in PACKAGES:
        errors.extend(_package_problems(directory, package, version))
    return errors


def main(argv: list[str]) -> int:
    if len(argv) != 2:
        print("usage: inspect_packages.py <directory> <version>", file=sys.stderr)
        return 2
    directory, version = pathlib.Path(argv[0]), argv[1]
    errors = problems(directory, version)
    for error in errors:
        print(f"ERROR: {error}")
    if errors:
        return 1
    print(f"OK: {', '.join(f'{package} {version}' for package in PACKAGES)} are ready to push")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
