"""Checks the packages a release is about to push to nuget.org, before anything is pushed.

Usage: python3 tools/release/inspect_packages.py <directory> <version>

A NuGet version can be unlisted but never deleted, so ci.yml runs this in pack-validate on every push and again in
publish-nuget just before the push. It fails when the directory does not hold exactly the six packages at the release
version, each with its .snupkg symbol package; when a package's DLL was built without -p:MinosRelease=true, which
Directory.Build.props versions <release>-local and which is compiled into the DLL; or when a package depends on another
Minos package at a different version. Standard library only.
"""
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
}


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


def _package_problems(directory: pathlib.Path, package: str, version: str) -> list[str]:
    errors: list[str] = []
    path = directory / f"{package}.{version}.nupkg"
    dll_name = f"lib/net10.0/{package}.dll"
    try:
        with zipfile.ZipFile(path) as archive:
            nuspec = archive.read(f"{package}.nuspec").decode("utf-8")
            dll = archive.read(dll_name) if dll_name in archive.namelist() else None
    except zipfile.BadZipFile as exc:
        return [f"{package}: {path.name} is not a valid package: {exc}"]
    except KeyError:
        return [f"{package}: {path.name} has no {package}.nuspec"]

    declared = re.search(r"<version>([^<]+)</version>", nuspec)
    if declared is None or declared.group(1) != version:
        errors.append(f"{package}: nuspec version is {declared.group(1) if declared else 'missing'}, expected {version}")

    if dll is None:
        errors.append(f"{package}: {dll_name} is missing")
    elif not _informational_version_found(dll, version):
        errors.append(f"{package}: {package}.dll has no informational version {version}; was it built with -p:MinosRelease=true?")

    if not (directory / f"{package}.{version}.snupkg").is_file():
        errors.append(f"{package}: {package}.{version}.snupkg is missing")

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
        return [f"expected exactly the six release packages in {directory}; missing {missing}, unexpected {extra}"]

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
