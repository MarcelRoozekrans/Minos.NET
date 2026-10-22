"""Unit tests for inspect_packages.py: fake packages built with zipfile, no dotnet needed."""
import io
import json
import pathlib
import tempfile
import unittest
import zipfile

import inspect_packages

VERSION = "1.0.0"
TOOL = "Minos.NET.Tool"
# The Minos assemblies the tool bundles in tools/net10.0/any.
TOOL_ASSEMBLIES = ["Minos.NET", "Minos.NET.SystemOne", "Minos.NET.Evaluation", "Minos.NET.Conformance", "Minos.NET.Tool"]


def informational(version: str) -> bytes:
    # A custom attribute blob: prolog 01 00, a length-prefixed UTF-8 string, then 00 00 for no named arguments.
    text = version.encode("utf-8")
    return b"\x01\x00" + bytes([len(text)]) + text + b"\x00\x00"


def write_package(directory: pathlib.Path, package_id: str, version: str, *,
                  dll_version: str | None = None, nuspec_version: str | None = None,
                  dependency_version: str | None = None, nuspec_id: str | None = None,
                  extra_dependency: str | None = None, dependency_xml: str | None = None,
                  symbols: bytes | None = None, with_dll: bool = True,
                  with_symbols: bool = True, drop_dependency: str | None = None,
                  package_type: str | None = None, bundled: dict[str, str] | None = None,
                  deps_extra: str | None = None, with_deps: bool = True) -> None:
    is_tool = package_id == TOOL
    dependencies = "".join(
        f'<dependency id="{dependency}" version="{dependency_version or version}" exclude="Build,Analyzers" />'
        for dependency in inspect_packages.PACKAGES[package_id]
        if dependency != drop_dependency)
    if extra_dependency:
        dependencies += f'<dependency id="{extra_dependency}" version="{version}" />'
    if dependency_xml is not None:
        dependencies = dependency_xml
    if is_tool:
        kind = "DotnetTool" if package_type is None else package_type
        package_types = f'<packageTypes><packageType name="{kind}" /></packageTypes>' if kind else ""
    else:
        package_types = ""
    nuspec = (f'<?xml version="1.0" encoding="utf-8"?><package><metadata><id>{nuspec_id or package_id}</id>'
              f'<version>{nuspec_version or version}</version>{package_types}'
              f'<dependencies><group targetFramework="net10.0">{dependencies}</group></dependencies>'
              f'</metadata></package>')
    # FileVersion is a four-part string that must never count as the informational version.
    dll = b"MZ fake " + b"\x01\x00\x071.0.0.0\x00\x00" + informational(dll_version or (version + "+abc1234"))
    with zipfile.ZipFile(directory / f"{package_id}.{version}.nupkg", "w") as archive:
        archive.writestr(f"{package_id}.nuspec", nuspec)
        if is_tool:
            if with_dll:
                versions = {name: version + "+abc1234" for name in TOOL_ASSEMBLIES}
                versions.update(bundled or {})
                for name, bundled_version in versions.items():
                    archive.writestr(f"tools/net10.0/any/{name}.dll", b"MZ fake " + informational(bundled_version))
            archive.writestr("tools/net10.0/any/System.CommandLine.dll", b"MZ third party")
            if with_deps:
                runtime = {f"{name}.dll": {} for name in TOOL_ASSEMBLIES}
                runtime["lib/net10.0/System.CommandLine.dll"] = {}
                if deps_extra:
                    runtime[deps_extra] = {}
                deps = {"targets": {".NETCoreApp,Version=v10.0": {"Minos.NET.Tool/1.0.0": {"runtime": runtime}}}}
                archive.writestr("tools/net10.0/any/Minos.NET.Tool.deps.json", json.dumps(deps))
        elif with_dll:
            archive.writestr(f"lib/net10.0/{package_id}.dll", dll)
    if with_symbols:
        if symbols is not None:
            (directory / f"{package_id}.{version}.snupkg").write_bytes(symbols)
        else:
            with zipfile.ZipFile(directory / f"{package_id}.{version}.snupkg", "w") as archive:
                archive.writestr(f"lib/net10.0/{package_id}.pdb", b"pdb")


class InspectPackagesTests(unittest.TestCase):
    def setUp(self) -> None:
        self._temp = tempfile.TemporaryDirectory()
        self.directory = pathlib.Path(self._temp.name)

    def tearDown(self) -> None:
        self._temp.cleanup()

    def write_all(self, version: str = VERSION, **overrides: dict) -> None:
        for package_id in inspect_packages.PACKAGES:
            write_package(self.directory, package_id, version, **overrides.get(package_id, {}))

    def test_nine_good_packages_pass(self) -> None:
        self.write_all()
        self.assertEqual(9, len(inspect_packages.PACKAGES))
        self.assertEqual([], inspect_packages.problems(self.directory, VERSION))

    def test_evaluation_must_depend_on_the_core(self) -> None:
        self.assertEqual(("Minos.NET",), inspect_packages.PACKAGES["Minos.NET.Evaluation"])
        self.write_all(**{"Minos.NET.Evaluation": {"drop_dependency": "Minos.NET"}})
        errors = inspect_packages.problems(self.directory, VERSION)
        self.assertTrue(any("Minos.NET.Evaluation does not depend on Minos.NET" in error for error in errors), errors)

    def test_recording_must_depend_on_the_core(self) -> None:
        self.assertEqual(("Minos.NET",), inspect_packages.PACKAGES["Minos.NET.Recording"])
        self.write_all(**{"Minos.NET.Recording": {"drop_dependency": "Minos.NET"}})
        errors = inspect_packages.problems(self.directory, VERSION)
        self.assertTrue(any("Minos.NET.Recording does not depend on Minos.NET" in error for error in errors), errors)

    def test_a_tool_package_of_the_wrong_type_fails(self) -> None:
        self.write_all(**{TOOL: {"package_type": "Dependency"}})
        errors = inspect_packages.problems(self.directory, VERSION)
        self.assertTrue(any(TOOL in error and "package type is Dependency, expected DotnetTool" in error for error in errors), errors)

    def test_a_tool_package_without_a_type_fails(self) -> None:
        self.write_all(**{TOOL: {"package_type": ""}})
        errors = inspect_packages.problems(self.directory, VERSION)
        self.assertTrue(any(TOOL in error and "package type is missing, expected DotnetTool" in error for error in errors), errors)

    def test_a_runtime_asset_the_deps_file_lists_but_the_package_lacks_fails(self) -> None:
        self.write_all(**{TOOL: {"deps_extra": "lib/net10.0/ZeroAlloc.Results.dll"}})
        errors = inspect_packages.problems(self.directory, VERSION)
        self.assertTrue(any(TOOL in error and "lists lib/net10.0/ZeroAlloc.Results.dll, which is not in" in error for error in errors), errors)

    def test_a_tool_without_a_deps_file_fails(self) -> None:
        self.write_all(**{TOOL: {"with_deps": False}})
        errors = inspect_packages.problems(self.directory, VERSION)
        self.assertTrue(any(TOOL in error and "deps.json is missing" in error for error in errors), errors)

    def test_a_stale_bundled_dll_in_the_tool_fails(self) -> None:
        self.write_all(**{TOOL: {"bundled": {"Minos.NET.Conformance": "1.0.0-local+abc1234"}}})
        errors = inspect_packages.problems(self.directory, VERSION)
        self.assertTrue(any(TOOL in error and "Minos.NET.Conformance.dll" in error and "MinosRelease" in error
                            for error in errors), errors)

    def test_a_stale_tool_assembly_fails(self) -> None:
        self.write_all(**{TOOL: {"bundled": {"Minos.NET.Tool": "0.9.0"}}})
        errors = inspect_packages.problems(self.directory, VERSION)
        self.assertTrue(any(TOOL in error and "Minos.NET.Tool.dll" in error for error in errors), errors)

    def test_a_tool_without_bundled_dlls_fails(self) -> None:
        self.write_all(**{TOOL: {"with_dll": False}})
        errors = inspect_packages.problems(self.directory, VERSION)
        self.assertTrue(any(TOOL in error and "tools/net10.0/any/Minos.NET.Tool.dll is missing" in error
                            for error in errors), errors)

    def test_a_minos_dependency_on_the_tool_fails(self) -> None:
        self.write_all(**{TOOL: {"extra_dependency": "Minos.NET"}})
        errors = inspect_packages.problems(self.directory, VERSION)
        self.assertTrue(any(TOOL in error and "depends on Minos.NET" in error for error in errors), errors)

    def test_a_missing_package_fails(self) -> None:
        self.write_all()
        (self.directory / f"Minos.NET.Caching.{VERSION}.nupkg").unlink()
        errors = inspect_packages.problems(self.directory, VERSION)
        self.assertTrue(any("Minos.NET.Caching" in error for error in errors), errors)

    def test_an_extra_package_fails(self) -> None:
        self.write_all()
        write_package(self.directory, "Minos.NET", "0.9.0")
        errors = inspect_packages.problems(self.directory, VERSION)
        self.assertTrue(any("unexpected" in error and "Minos.NET.0.9.0.nupkg" in error for error in errors), errors)

    def test_a_missing_symbol_package_fails(self) -> None:
        self.write_all(**{"Minos.NET.OpenAI": {"with_symbols": False}})
        errors = inspect_packages.problems(self.directory, VERSION)
        self.assertTrue(any("Minos.NET.OpenAI" in error and ".snupkg" in error for error in errors), errors)

    def test_a_tool_without_a_symbol_package_fails(self) -> None:
        self.write_all(**{TOOL: {"with_symbols": False}})
        errors = inspect_packages.problems(self.directory, VERSION)
        self.assertTrue(any(TOOL in error and ".snupkg" in error for error in errors), errors)

    def test_a_local_build_fails(self) -> None:
        self.write_all(**{"Minos.NET.SystemOne": {"dll_version": "1.0.0-local+abc1234"}})
        errors = inspect_packages.problems(self.directory, VERSION)
        self.assertTrue(any("Minos.NET.SystemOne" in error and "MinosRelease" in error for error in errors), errors)

    def test_a_longer_version_ending_in_the_release_does_not_count(self) -> None:
        self.write_all(**{"Minos.NET": {"dll_version": "11.0.0"}})
        self.assertNotEqual([], inspect_packages.problems(self.directory, VERSION))

    def test_a_missing_dll_fails(self) -> None:
        self.write_all(**{"Minos.NET.RateLimiting": {"with_dll": False}})
        errors = inspect_packages.problems(self.directory, VERSION)
        self.assertTrue(any("Minos.NET.RateLimiting" in error and "lib/net10.0/Minos.NET.RateLimiting.dll is missing" in error
                            for error in errors), errors)

    def test_a_nuspec_at_another_version_fails(self) -> None:
        self.write_all(**{"Minos.NET.DependencyInjection": {"nuspec_version": "1.0.1"}})
        errors = inspect_packages.problems(self.directory, VERSION)
        self.assertTrue(any("Minos.NET.DependencyInjection" in error and "nuspec version is 1.0.1" in error
                            for error in errors), errors)

    def test_a_dependency_on_another_minos_version_fails(self) -> None:
        self.write_all(**{"Minos.NET.SystemOne": {"dependency_version": "0.13.0"}})
        errors = inspect_packages.problems(self.directory, VERSION)
        self.assertTrue(any("Minos.NET.SystemOne" in error and "0.13.0" in error for error in errors), errors)

    def test_a_range_starting_at_the_release_is_accepted(self) -> None:
        self.write_all(**{"Minos.NET.Caching": {"dependency_version": f"[{VERSION}, )"}})
        self.assertEqual([], inspect_packages.problems(self.directory, VERSION))

    def test_a_missing_minos_dependency_fails(self) -> None:
        self.write_all(**{"Minos.NET.OpenAI": {"drop_dependency": "Minos.NET.DependencyInjection"}})
        errors = inspect_packages.problems(self.directory, VERSION)
        self.assertTrue(any("Minos.NET.OpenAI" in error and "Minos.NET.DependencyInjection" in error for error in errors), errors)

    def test_an_unexpected_minos_dependency_fails(self) -> None:
        self.write_all(**{"Minos.NET": {"extra_dependency": "Minos.NET.Generator"}})
        errors = inspect_packages.problems(self.directory, VERSION)
        self.assertTrue(any("Minos.NET depends on Minos.NET.Generator, which is not one of its expected Minos dependencies" in error
                            for error in errors), errors)

    def test_an_empty_symbol_package_fails(self) -> None:
        self.write_all(**{"Minos.NET.Caching": {"symbols": b""}})
        errors = inspect_packages.problems(self.directory, VERSION)
        self.assertTrue(any("Minos.NET.Caching" in error and "not a valid symbol package" in error for error in errors), errors)

    def test_a_symbol_package_without_the_pdb_fails(self) -> None:
        buffer = io.BytesIO()
        with zipfile.ZipFile(buffer, "w") as archive:
            archive.writestr("readme.txt", b"x")
        self.write_all(**{"Minos.NET.Caching": {"symbols": buffer.getvalue()}})
        errors = inspect_packages.problems(self.directory, VERSION)
        self.assertTrue(any("Minos.NET.Caching" in error and "has no Minos.NET.Caching.pdb" in error for error in errors), errors)

    def test_a_nuspec_with_another_id_fails(self) -> None:
        self.write_all(**{"Minos.NET.Caching": {"nuspec_id": "Minos.NET.Other"}})
        errors = inspect_packages.problems(self.directory, VERSION)
        self.assertTrue(any("Minos.NET.Caching" in error and "nuspec id is Minos.NET.Other" in error for error in errors), errors)

    def test_reordered_attributes_and_a_split_element_are_accepted(self) -> None:
        split = (f'<dependency\n    exclude="Build"\n    version="{VERSION}"\n'
                 '    id="Minos.NET" />')
        self.write_all(**{"Minos.NET.Caching": {"dependency_xml": split}})
        self.assertEqual([], inspect_packages.problems(self.directory, VERSION))

    def test_a_minos_dependency_without_a_version_fails(self) -> None:
        self.write_all(**{"Minos.NET.Caching": {"dependency_xml": '<dependency id="Minos.NET" />'}})
        errors = inspect_packages.problems(self.directory, VERSION)
        self.assertTrue(any("Minos.NET.Caching" in error and "(no version)" in error for error in errors), errors)

    def test_main_reports_usage(self) -> None:
        self.assertEqual(2, inspect_packages.main([]))


if __name__ == "__main__":
    unittest.main()
