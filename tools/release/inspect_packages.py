"""Checks the packages a release is about to push to nuget.org, before anything is pushed.

Usage: python3 tools/release/inspect_packages.py <directory> <version>

A NuGet version can be unlisted but never deleted, so ci.yml runs this in pack-validate on every push and again in
publish-nuget just before the push. It fails when the directory does not hold exactly the ten packages at the release
version; when a package's DLL was built without -p:MinosRelease=true, which Directory.Build.props versions
<release>-local and which is compiled into the DLL; or when a package depends on another Minos package at a different
version. Minos.NET.Tool is a dotnet tool: it must have the DotnetTool package type and no Minos dependencies, because it
bundles its Minos assemblies in tools/<framework>/any, and each of those must carry the release version. Every package
is inspected for each framework in FRAMEWORKS, so a package that lacks the net11.0 build fails.

It also fails when a Minos DLL in a package has no embedded PDB whose Source Link maps it to this repository on GitHub,
which Go to Definition into the package needs. That check is scripts/check-embedded-pdb/check-embedded-pdb.cs, which
reads the PDB with System.Reflection.Metadata, so this script runs it with dotnet run and needs a .NET SDK on the PATH.
Standard library only.
"""
import json
import pathlib
import re
import subprocess
import sys
import tempfile
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
# The target frameworks every package ships a build for, in lib/<framework> or tools/<framework>/any.
FRAMEWORKS = ("net10.0", "net11.0")

# The Source Link map in each embedded PDB sends the debugger to this prefix, followed by the commit and the path.
SOURCE_LINK_PREFIX = "https://raw.githubusercontent.com/MarcelRoozekrans/Minos.NET/"
# The repository's System.Reflection.Metadata check of each DLL's embedded PDB and Source Link map.
ROOT = pathlib.Path(__file__).resolve().parents[2]
CHECKER = "scripts/check-embedded-pdb/check-embedded-pdb.cs"


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


def _runtime_asset_problems(package: str, directory: str, deps: bytes | None, packaged: set[str]) -> list[str]:
    # The tool starts from its deps.json, which lists every assembly it loads. A listed assembly that is not in the
    # package fails only when that code path first runs, so --help alone does not show it.
    if deps is None:
        return [f"{package}: {directory}{package}.deps.json is missing"]
    try:
        targets = json.loads(deps)["targets"]
    except (ValueError, KeyError) as exc:
        return [f"{package}: {package}.deps.json cannot be read: {exc}"]
    missing: set[str] = set()
    for libraries in targets.values():
        for library in libraries.values():
            for asset in library.get("runtime", {}):
                if f"{directory}{asset.rsplit('/', 1)[-1]}" not in packaged:
                    missing.add(asset)
    return [f"{package}: {package}.deps.json lists {asset}, which is not in {directory}" for asset in sorted(missing)]


def _package_problems(directory: pathlib.Path, package: str, version: str) -> list[str]:
    errors: list[str] = []
    path = directory / f"{package}.{version}.nupkg"
    is_tool = package == TOOL
    # The assembly each framework's build ships, which must carry the release version.
    dll_names = {
        framework: f"tools/{framework}/any/{package}.dll" if is_tool else f"lib/{framework}/{package}.dll"
        for framework in FRAMEWORKS
    }
    dlls: dict[str, bytes | None] = {}
    bundled: dict[str, bytes] = {}
    deps_by_directory: dict[str, bytes | None] = {}
    packaged: set[str] = set()
    try:
        with zipfile.ZipFile(path) as archive:
            nuspec = archive.read(f"{package}.nuspec").decode("utf-8")
            names = set(archive.namelist())
            for framework, dll_name in dll_names.items():
                dlls[framework] = archive.read(dll_name) if dll_name in names else None
            if is_tool:
                packaged = names
                for framework in FRAMEWORKS:
                    directory_name = f"tools/{framework}/any/"
                    deps_name = f"{directory_name}{package}.deps.json"
                    deps_by_directory[directory_name] = archive.read(deps_name) if deps_name in names else None
                    for name in sorted(names):
                        if name.startswith(f"{directory_name}Minos.NET") and name.endswith(".dll"):
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

    for framework, dll_name in dll_names.items():
        dll = dlls[framework]
        if dll is None:
            errors.append(f"{package}: {dll_name} is missing")
        elif not _informational_version_found(dll, version):
            errors.append(f"{package}: {dll_name} has no informational version {version}; was it built with -p:MinosRelease=true?")

    if is_tool:
        types = re.findall(r'<packageType\b[^>]*\bname="([^"]+)"', nuspec)
        if types != ["DotnetTool"]:
            errors.append(f"{package}: package type is {', '.join(types) or 'missing'}, expected DotnetTool")
        for directory_name, deps in deps_by_directory.items():
            errors.extend(_runtime_asset_problems(package, directory_name, deps, packaged))
        for name, content in sorted(bundled.items()):
            if name not in dll_names.values() and not _informational_version_found(content, version):
                errors.append(f"{package}: bundled {name} has no informational version {version}; was it built with -p:MinosRelease=true?")

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


def own_dlls(directory: pathlib.Path, version: str, destination: pathlib.Path) -> list[pathlib.Path]:
    """Extracts every Minos assembly of the release packages, in lib/, analyzers/ or tools/, to destination/<package>/
    and returns their paths. Third-party assemblies the tool bundles are not ours, so they are left out."""
    extracted: list[pathlib.Path] = []
    for package in PACKAGES:
        path = directory / f"{package}.{version}.nupkg"
        if not path.is_file():
            continue
        with zipfile.ZipFile(path) as archive:
            for name in sorted(archive.namelist()):
                if (name.startswith(("lib/", "analyzers/", "tools/")) and name.endswith(".dll")
                        and name.rsplit("/", 1)[-1].startswith("Minos.NET")):
                    extracted.append(pathlib.Path(archive.extract(name, destination / package)))
    return extracted


def check_embedded_pdbs(dlls: list[pathlib.Path]) -> bool:
    """Runs the System.Reflection.Metadata checker over the DLLs: each must embed its PDB, be a deterministic build, and
    have a Source Link map that sends every source document to SOURCE_LINK_PREFIX. It prints one line per DLL."""
    if not dlls:
        print("ERROR: the packages hold no Minos DLL to check for an embedded PDB")
        return False
    command = ["dotnet", "run", CHECKER, "--", SOURCE_LINK_PREFIX, *(str(dll) for dll in dlls)]
    sys.stdout.flush()  # keeps this script's lines ahead of the checker's in a log
    return subprocess.run(command, cwd=ROOT, check=False).returncode == 0


def main(argv: list[str]) -> int:
    if len(argv) != 2:
        print("usage: inspect_packages.py <directory> <version>", file=sys.stderr)
        return 2
    directory, version = pathlib.Path(argv[0]), argv[1]
    errors = problems(directory, version)
    for error in errors:
        print(f"ERROR: {error}")
    with tempfile.TemporaryDirectory() as extracted:
        pdbs_ok = check_embedded_pdbs(own_dlls(directory, version, pathlib.Path(extracted)))
    if errors or not pdbs_ok:
        return 1
    print(f"OK: {', '.join(f'{package} {version}' for package in PACKAGES)} are ready to push")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
