"""Unit tests for ship_release_tracking.py: a temporary tree, no dotnet needed."""
import os
import pathlib
import tempfile
import unittest

import ship_release_tracking as ship


class UnpackableProjectTests(unittest.TestCase):
    def make_project(self, root: pathlib.Path, name: str, packable_xml: str) -> None:
        directory = root / "src" / name
        directory.mkdir(parents=True)
        (directory / f"{name}.csproj").write_text(f"<Project><PropertyGroup>{packable_xml}</PropertyGroup></Project>")
        (directory / "PublicAPI.Shipped.txt").write_text("#nullable enable\n")
        (directory / "PublicAPI.Unshipped.txt").write_text("#nullable enable\nMinos.Thing\n")

    def in_directory(self, root: pathlib.Path):
        previous = os.getcwd()
        os.chdir(root)
        self.addCleanup(os.chdir, previous)

    def test_unshipped_api_of_an_unpackable_project_is_not_tracked(self):
        with tempfile.TemporaryDirectory() as temp:
            root = pathlib.Path(temp)
            self.make_project(root, "Packed", "")
            self.make_project(root, "Unpacked", "<IsPackable>false</IsPackable>")
            self.in_directory(root)

            paths = [pathlib.PurePath(p).parent.name for p in ship.api_unshipped_paths()]
            entries = ship.unshipped_entries()

            self.assertEqual(["Packed"], paths)
            self.assertEqual(1, len(entries))
            self.assertIn("Packed", entries[0])


if __name__ == "__main__":
    unittest.main()
