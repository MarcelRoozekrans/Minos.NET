import csv
import hashlib
import json
import tempfile
import unittest
from pathlib import Path

import build

HERE = Path(__file__).resolve().parent


class BuildTests(unittest.TestCase):
    def test_building_twice_gives_byte_identical_files(self):
        with tempfile.TemporaryDirectory() as a, tempfile.TemporaryDirectory() as b:
            build.build(HERE / "source", HERE / "urgency-labels.csv", Path(a))
            build.build(HERE / "source", HERE / "urgency-labels.csv", Path(b))
            for name in (build.MAIN_FILE, build.URGENCY_FILE):
                self.assertEqual((Path(a) / name).read_bytes(), (Path(b) / name).read_bytes())

    def test_the_committed_files_match_a_fresh_build(self):
        with tempfile.TemporaryDirectory() as a:
            build.build(HERE / "source", HERE / "urgency-labels.csv", Path(a))
            for name in (build.MAIN_FILE, build.URGENCY_FILE):
                self.assertEqual((Path(a) / name).read_bytes(), (HERE / name).read_bytes())

    def test_output_is_utf8_without_bom_and_uses_unix_line_endings(self):
        for name in (build.MAIN_FILE, build.URGENCY_FILE):
            data = (HERE / name).read_bytes()
            self.assertFalse(data.startswith(b"\xef\xbb\xbf"))
            self.assertNotIn(b"\r", data)
            self.assertTrue(data.endswith(b"\n"))

    def test_counts_are_400_and_60(self):
        main = (HERE / build.MAIN_FILE).read_text(encoding="utf-8").splitlines()
        urgency = (HERE / build.URGENCY_FILE).read_text(encoding="utf-8").splitlines()
        self.assertEqual(400, len(main))
        self.assertEqual(60, len(urgency))
        for line in main + urgency:
            json.loads(line)

    def test_every_urgency_text_has_a_label_and_no_label_is_unused(self):
        urgency = [json.loads(l) for l in (HERE / build.URGENCY_FILE).read_text(encoding="utf-8").splitlines()]
        used = {hashlib.sha256(i["state"].encode("utf-8")).hexdigest() for i in urgency}
        with open(HERE / "urgency-labels.csv", encoding="utf-8", newline="") as f:
            labels = list(csv.DictReader(f))
        self.assertEqual(["text_sha256", "urgency"], list(labels[0].keys()))
        hashes = [r["text_sha256"] for r in labels]
        self.assertEqual(len(hashes), len(set(hashes)))
        self.assertEqual(used, set(hashes))
        self.assertEqual(60, len(labels))
        self.assertTrue(all(r["urgency"] in ("0", "1", "2") for r in labels))

    def test_source_is_the_upstream_row_number(self):
        with open(HERE / "source" / build.SUBSET_NAME, encoding="utf-8", newline="") as f:
            rows = list(csv.DictReader(f))
        numbers = [int(r["row"]) for r in rows]
        self.assertEqual(numbers, sorted(set(numbers)))
        self.assertTrue(1 <= numbers[0] and numbers[-1] <= 3080)
        main = [json.loads(l) for l in (HERE / build.MAIN_FILE).read_text(encoding="utf-8").splitlines()]
        self.assertEqual([f"banking77/test#{n}" for n in numbers], [i["source"] for i in main])

    def test_a_missing_label_is_an_error(self):
        with tempfile.TemporaryDirectory() as a:
            empty = Path(a) / "labels.csv"
            empty.write_text("text_sha256,urgency\n", encoding="utf-8", newline="")
            with self.assertRaises(SystemExit):
                build.build(HERE / "source", empty, Path(a))


if __name__ == "__main__":
    unittest.main()
