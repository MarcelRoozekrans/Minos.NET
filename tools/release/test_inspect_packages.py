"""Unit tests for inspect_packages.py: fake packages built with zipfile, no dotnet needed. The embedded PDB check is
scripts/check-embedded-pdb/check-embedded-pdb.cs; these tests only check which DLLs it is given, with dotnet mocked."""
import contextlib
import io
import json
import pathlib
import subprocess
import tempfile
import unittest
import zipfile
from unittest import mock

import inspect_packages

COMMIT = "0123456789abcdef0123456789abcdef01234567"
VERSION = "1.0.0"
TOOL = "Minos.NET.Tool"
# The Minos assemblies the tool bundles in tools/<framework>/any.
TOOL_ASSEMBLIES = ["Minos.NET", "Minos.NET.SystemOne", "Minos.NET.Evaluation", "Minos.NET.Recording", "Minos.NET.Conformance", "Minos.NET.Tool"]


def informational(version: str) -> bytes:
    # A custom attribute blob: prolog 01 00, a length-prefixed UTF-8 string, then 00 00 for no named arguments.
    text = version.encode("utf-8")
    return b"\x01\x00" + bytes([len(text)]) + text + b"\x00\x00"


def fake_dll(content: bytes) -> bytes:
    """Stands in for a DLL: the inspection only searches its bytes for the informational version attribute."""
    return b"MZ" + content


def write_package(directory: pathlib.Path, package_id: str, version: str, *,
                  dll_version: str | None = None, nuspec_version: str | None = None,
                  dependency_version: str | None = None, nuspec_id: str | None = None,
                  extra_dependency: str | None = None, dependency_xml: str | None = None,
                  with_dll: bool = True,
                  extra_dlls: dict[str, bytes] | None = None, drop_dependency: str | None = None,
                  package_type: str | None = None, bundled: dict[str, str] | None = None,
                  deps_extra: str | None = None, with_deps: bool = True,
                  without_framework: str | None = None) -> None:
    is_tool = package_id == TOOL
    frameworks = [framework for framework in inspect_packages.FRAMEWORKS if framework != without_framework]
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
    dll = fake_dll(b"\x01\x00\x071.0.0.0\x00\x00" + informational(dll_version or (version + "+abc1234")))
    with zipfile.ZipFile(directory / f"{package_id}.{version}.nupkg", "w") as archive:
        archive.writestr(f"{package_id}.nuspec", nuspec)
        for framework in frameworks:
            if is_tool:
                if with_dll:
                    versions = {name: version + "+abc1234" for name in TOOL_ASSEMBLIES}
                    versions.update(bundled or {})
                    for name, bundled_version in versions.items():
                        archive.writestr(f"tools/{framework}/any/{name}.dll", fake_dll(informational(bundled_version)))
                archive.writestr(f"tools/{framework}/any/System.CommandLine.dll", b"MZ third party")
                if with_deps:
                    runtime = {f"{name}.dll": {} for name in TOOL_ASSEMBLIES}
                    runtime["lib/net10.0/System.CommandLine.dll"] = {}
                    if deps_extra:
                        runtime[deps_extra] = {}
                    deps = {"targets": {".NETCoreApp,Version=v10.0": {"Minos.NET.Tool/1.0.0": {"runtime": runtime}}}}
                    archive.writestr(f"tools/{framework}/any/Minos.NET.Tool.deps.json", json.dumps(deps))
            elif with_dll:
                archive.writestr(f"lib/{framework}/{package_id}.dll", dll)
        for name, content in (extra_dlls or {}).items():
            archive.writestr(name, content)


class InspectPackagesTests(unittest.TestCase):
    def setUp(self) -> None:
        self._temp = tempfile.TemporaryDirectory()
        self.directory = pathlib.Path(self._temp.name)

    def tearDown(self) -> None:
        self._temp.cleanup()

    def write_all(self, version: str = VERSION, **overrides: dict) -> None:
        for package_id in inspect_packages.PACKAGES:
            write_package(self.directory, package_id, version, **overrides.get(package_id, {}))

    def test_ten_good_packages_pass(self) -> None:
        self.write_all()
        self.assertEqual(10, len(inspect_packages.PACKAGES))
        self.assertEqual([], inspect_packages.problems(self.directory, VERSION))

    def test_evaluation_must_depend_on_the_core_and_recording(self) -> None:
        self.assertEqual(("Minos.NET", "Minos.NET.Recording"), inspect_packages.PACKAGES["Minos.NET.Evaluation"])
        self.write_all(**{"Minos.NET.Evaluation": {"drop_dependency": "Minos.NET"}})
        errors = inspect_packages.problems(self.directory, VERSION)
        self.assertTrue(any("Minos.NET.Evaluation does not depend on Minos.NET" in error for error in errors), errors)

    def test_evaluation_without_its_recording_dependency_fails(self) -> None:
        self.write_all(**{"Minos.NET.Evaluation": {"drop_dependency": "Minos.NET.Recording"}})
        errors = inspect_packages.problems(self.directory, VERSION)
        self.assertTrue(any("Minos.NET.Evaluation does not depend on Minos.NET.Recording" in error for error in errors), errors)

    def test_recording_must_depend_on_the_core(self) -> None:
        self.assertEqual(("Minos.NET",), inspect_packages.PACKAGES["Minos.NET.Recording"])
        self.write_all(**{"Minos.NET.Recording": {"drop_dependency": "Minos.NET"}})
        errors = inspect_packages.problems(self.directory, VERSION)
        self.assertTrue(any("Minos.NET.Recording does not depend on Minos.NET" in error for error in errors), errors)

    def test_extensions_ai_must_depend_on_the_core_and_dependency_injection(self) -> None:
        self.assertEqual(("Minos.NET", "Minos.NET.DependencyInjection"), inspect_packages.PACKAGES["Minos.NET.Extensions.AI"])
        self.write_all(**{"Minos.NET.Extensions.AI": {"drop_dependency": "Minos.NET.DependencyInjection"}})
        errors = inspect_packages.problems(self.directory, VERSION)
        self.assertTrue(
            any("Minos.NET.Extensions.AI does not depend on Minos.NET.DependencyInjection" in error for error in errors), errors)

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

    def test_a_library_without_its_net11_build_fails(self) -> None:
        self.write_all(**{"Minos.NET.Caching": {"without_framework": "net11.0"}})
        errors = inspect_packages.problems(self.directory, VERSION)
        self.assertTrue(any("Minos.NET.Caching" in error and "lib/net11.0/Minos.NET.Caching.dll is missing" in error
                            for error in errors), errors)

    def test_a_tool_without_its_net11_payload_fails(self) -> None:
        self.write_all(**{TOOL: {"without_framework": "net11.0"}})
        errors = inspect_packages.problems(self.directory, VERSION)
        self.assertTrue(any(TOOL in error and "tools/net11.0/any/Minos.NET.Tool.dll is missing" in error for error in errors), errors)
        self.assertTrue(any(TOOL in error and "tools/net11.0/any/Minos.NET.Tool.deps.json is missing" in error for error in errors), errors)

    def test_a_tool_runtime_asset_missing_from_the_net11_payload_fails(self) -> None:
        self.write_all()
        path = self.directory / f"{TOOL}.{VERSION}.nupkg"
        with zipfile.ZipFile(path) as source:
            entries = {name: source.read(name) for name in source.namelist()}
        deps = json.loads(entries["tools/net11.0/any/Minos.NET.Tool.deps.json"])
        deps["targets"][".NETCoreApp,Version=v10.0"]["Minos.NET.Tool/1.0.0"]["runtime"]["Only.Net11.dll"] = {}
        entries["tools/net11.0/any/Minos.NET.Tool.deps.json"] = json.dumps(deps).encode("utf-8")
        with zipfile.ZipFile(path, "w") as target:
            for name, content in entries.items():
                target.writestr(name, content)
        errors = inspect_packages.problems(self.directory, VERSION)
        self.assertTrue(any(TOOL in error and "Only.Net11.dll" in error and "tools/net11.0/any/" in error for error in errors), errors)

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

    def test_own_dlls_are_the_minos_assemblies_in_lib_analyzers_and_tools(self) -> None:
        analyzer = "analyzers/dotnet/cs/Minos.NET.Generator.dll"
        self.write_all(**{"Minos.NET": {"extra_dlls": {analyzer: fake_dll(b"")}}})
        with tempfile.TemporaryDirectory() as extracted:
            destination = pathlib.Path(extracted)
            dlls = inspect_packages.own_dlls(self.directory, VERSION, destination)
            self.assertTrue(all(dll.is_file() for dll in dlls), dlls)
            names = {dll.relative_to(destination).as_posix() for dll in dlls}
        self.assertIn(f"Minos.NET/{analyzer}", names)
        self.assertIn("Minos.NET.Caching/lib/net11.0/Minos.NET.Caching.dll", names)
        self.assertIn(f"{TOOL}/tools/net10.0/any/Minos.NET.Conformance.dll", names)
        # Ten packages: nine libraries with two frameworks each, the tool with its six assemblies per framework, and the analyzer.
        self.assertEqual(9 * 2 + len(TOOL_ASSEMBLIES) * 2 + 1, len(names))
        self.assertFalse(any("System.CommandLine" in name for name in names), names)

    def test_the_checker_gets_the_prefix_and_every_dll(self) -> None:
        dlls = [pathlib.Path("a/Minos.NET.dll"), pathlib.Path("b/Minos.NET.Tool.dll")]
        with mock.patch.object(subprocess, "run", return_value=subprocess.CompletedProcess([], 0)) as run:
            self.assertTrue(inspect_packages.check_embedded_pdbs(dlls, COMMIT))
        command = run.call_args.args[0]
        # The prefix carries the commit, so a map to main or to another commit fails the checker.
        prefix = f"https://raw.githubusercontent.com/MarcelRoozekrans/Minos.NET/{COMMIT}/"
        self.assertEqual(["dotnet", "run", inspect_packages.CHECKER, "--", prefix], command[:5])
        self.assertEqual([str(dll) for dll in dlls], command[5:])
        self.assertEqual(inspect_packages.ROOT, run.call_args.kwargs["cwd"])

    def test_no_dll_to_check_fails(self) -> None:
        with mock.patch.object(subprocess, "run") as run:
            with contextlib.redirect_stdout(io.StringIO()):
                self.assertFalse(inspect_packages.check_embedded_pdbs([], COMMIT))
        run.assert_not_called()

    def test_main_passes_when_the_packages_and_the_checker_pass(self) -> None:
        self.write_all()
        with mock.patch.object(subprocess, "run", return_value=subprocess.CompletedProcess([], 0)), \
                mock.patch.object(inspect_packages, "head_commit", return_value=COMMIT):
            with contextlib.redirect_stdout(io.StringIO()):
                self.assertEqual(0, inspect_packages.main([str(self.directory), VERSION]))

    def test_main_fails_when_the_checker_fails(self) -> None:
        self.write_all()
        with mock.patch.object(subprocess, "run", return_value=subprocess.CompletedProcess([], 1)), \
                mock.patch.object(inspect_packages, "head_commit", return_value=COMMIT):
            self.assertEqual(1, inspect_packages.main([str(self.directory), VERSION]))

    def test_main_pins_the_prefix_to_the_commit_it_reads(self) -> None:
        self.write_all()
        with mock.patch.object(subprocess, "run", return_value=subprocess.CompletedProcess([], 0)) as run, \
                mock.patch.object(inspect_packages, "head_commit", return_value=COMMIT), \
                contextlib.redirect_stdout(io.StringIO()):
            self.assertEqual(0, inspect_packages.main([str(self.directory), VERSION]))
        self.assertEqual(f"https://raw.githubusercontent.com/MarcelRoozekrans/Minos.NET/{COMMIT}/", run.call_args.args[0][4])

    def test_main_fails_when_git_gives_no_commit(self) -> None:
        self.write_all()
        with mock.patch.object(subprocess, "run") as run, mock.patch.object(inspect_packages, "head_commit", return_value=None), \
                contextlib.redirect_stdout(io.StringIO()):
            self.assertEqual(1, inspect_packages.main([str(self.directory), VERSION]))
        run.assert_not_called()

    def test_head_commit_reads_the_full_sha(self) -> None:
        done = subprocess.CompletedProcess([], 0, stdout=COMMIT + "\n")
        with mock.patch.object(subprocess, "run", return_value=done) as run:
            self.assertEqual(COMMIT, inspect_packages.head_commit())
        self.assertEqual(["git", "rev-parse", "HEAD"], run.call_args.args[0])
        self.assertEqual(inspect_packages.ROOT, run.call_args.kwargs["cwd"])

    def test_head_commit_rejects_a_failed_or_odd_answer(self) -> None:
        for done in (subprocess.CompletedProcess([], 128, stdout=""), subprocess.CompletedProcess([], 0, stdout="HEAD\n")):
            with mock.patch.object(subprocess, "run", return_value=done):
                self.assertIsNone(inspect_packages.head_commit())

    def test_the_checker_script_exists(self) -> None:
        self.assertTrue((inspect_packages.ROOT / inspect_packages.CHECKER).is_file())


if __name__ == "__main__":
    unittest.main()
