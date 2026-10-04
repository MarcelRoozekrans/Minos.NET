"""Tests for merge.py, on sample result files in the shared format: two runtimes, a null byte count and notes."""

import json
import os
import subprocess
import sys

import pytest

sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))

import merge  # noqa: E402

MERGE = os.path.join(os.path.dirname(__file__), "..", "merge.py")


def machine(**overrides):
    value = {
        "name": "box",
        "os": "Microsoft Windows 10.0.26200",
        "cpu": "Intel Core Ultra 7 155H",
        "date": "2026-10-04T10:00:00Z",
        "mockCeilingPerSecond": None,
        "cores": "0-11",
        "mockCores": "12-19",
    }
    value.update(overrides)
    return value


def result(client, library, runtime, runtime_version, throughput, allocated, mean=0.25, note=None):
    value = {
        "client": client,
        "library": library,
        "version": "1.2.3",
        "runtime": runtime,
        "runtimeVersion": runtime_version,
        "latencyMs": {"mean": mean, "p50": 0.2, "p99": 1.5},
        "throughputPerSecond": throughput,
        "concurrency": 16,
        "allocatedBytesPerCall": allocated,
    }
    if note is not None:
        value["note"] = note
    return value


def dotnet_file():
    return {
        "machine": machine(mockCeilingPerSecond=100000.0, os="Microsoft Windows 10.0.26200 .NET", date="2026-10-04T10:05:00Z"),
        "results": [
            result("jev", "ZeroAlloc.Jev", ".NET", "10.0.0", 50000.0, 1024),
            result("jevsharp", "JevSharp", ".NET", "10.0.0", 40000.4, 4096, note="Sends no auth header."),
            result("raw", "HttpClient, System.Text.Json", ".NET", "10.0.0", 60000.0, 800),
        ],
    }


def js_file():
    return {
        "machine": machine(cpu="Intel(R) Core(TM) Ultra 7 155H", date="2026-10-04T10:06:00Z"),
        "results": [result("typesafe-ai-sdk-js", "@typesafe-ai/sdk", "Node.js", "24.1.0", 4940.0, None, mean=0.5)],
    }


def write(tmp_path, name, content):
    path = tmp_path / name
    path.write_text(json.dumps(content), encoding="utf-8")
    return str(path)


@pytest.fixture
def files(tmp_path):
    return [write(tmp_path, "dotnet-box.json", dotnet_file()), write(tmp_path, "js-box.json", js_file())]


def table_rows(text):
    return [line for line in text.splitlines() if line.startswith("| ") and not line.startswith("| Client")]


def test_rows_are_sorted_by_throughput_with_every_column(files):
    text = merge.render(merge.load(files), "ZeroAlloc.Jev")

    assert table_rows(text) == [
        "| raw | HttpClient, System.Text.Json 1.2.3 | .NET 10.0.0 | 0.250 | 0.200 | 1.500 | 60,000 | 60% | 800 |",
        "| **jev** | ZeroAlloc.Jev 1.2.3 | .NET 10.0.0 | 0.250 | 0.200 | 1.500 | 50,000 | 50% | 1,024 |",
        "| jevsharp[^1] | JevSharp 1.2.3 | .NET 10.0.0 | 0.250 | 0.200 | 1.500 | 40,000 | 40% | 4,096 |",
        "| typesafe-ai-sdk-js | @typesafe-ai/sdk 1.2.3 | Node.js 24.1.0 | 0.500 | 0.200 | 1.500 | 4,940 | 5% | — |",
    ]


def test_the_header_names_the_project_and_every_column(files):
    lines = merge.render(merge.load(files), "ZeroAlloc.Jev").splitlines()

    assert lines[0] == "### ZeroAlloc.Jev: client comparison"
    assert lines[2] == (
        "| Client | Library | Runtime | Mean (ms) | p50 (ms) | p99 (ms) | Throughput (/s) | Of mock ceiling | Bytes/call |"
    )


def test_the_machine_line_and_the_footnotes_follow_the_table(files):
    text = merge.render(merge.load(files), "ZeroAlloc.Jev")

    # The OS and CPU come from the file with the ceiling; the date is the latest.
    assert (
        "Machine: box; OS: Microsoft Windows 10.0.26200 .NET; CPU: Intel Core Ultra 7 155H; date: 2026-10-04T10:06:00Z; "
        "mock cores: 12-19; client cores: 0-11; mock ceiling: 100,000/s."
    ) in text.splitlines()
    assert text.endswith("\n[^1]: Sends no auth header.\n")


def test_harnesses_on_different_cores_are_listed_by_runtime(tmp_path):
    js = js_file()
    js["machine"]["cores"] = None
    files = [write(tmp_path, "dotnet-box.json", dotnet_file()), write(tmp_path, "js-box.json", js)]

    text = merge.render(merge.load(files), "ZeroAlloc.Jev")

    assert "client cores: 0-11 (.NET); unpinned (Node.js);" in text


def test_without_a_ceiling_the_share_is_a_dash(tmp_path):
    text = merge.render(merge.load([write(tmp_path, "js-box.json", js_file())]), "ZeroAlloc.Jev")

    assert table_rows(text)[0].split(" | ")[7] == "—"
    assert "mock ceiling: —." in text


def test_files_from_two_machines_are_refused(tmp_path):
    js = js_file()
    js["machine"]["name"] = "other"
    files = [write(tmp_path, "dotnet-box.json", dotnet_file()), write(tmp_path, "js-other.json", js)]

    with pytest.raises(merge.MergeError, match="machine name"):
        merge.render(merge.load(files), "ZeroAlloc.Jev")


def test_files_with_two_mock_core_sets_are_refused(tmp_path):
    js = js_file()
    js["machine"]["mockCores"] = "0-3"
    files = [write(tmp_path, "dotnet-box.json", dotnet_file()), write(tmp_path, "js-box.json", js)]

    with pytest.raises(merge.MergeError, match="mock's cores"):
        merge.render(merge.load(files), "ZeroAlloc.Jev")


def test_the_command_line_prints_the_table(files):
    run = subprocess.run(
        [sys.executable, MERGE, *files, "--project", "ZeroAlloc.Jev"], capture_output=True, check=False
    )

    assert run.returncode == 0, run.stderr
    out = run.stdout.decode("utf-8")
    assert out.startswith("### ZeroAlloc.Jev: client comparison\n")
    assert "| typesafe-ai-sdk-js |" in out and "— |" in out


@pytest.mark.parametrize(
    "args",
    [
        ["--project", "ZeroAlloc.Jev"],
        ["missing.json", "--project", "ZeroAlloc.Jev"],
        ["{file}"],
        ["{file}", "--project", " "],
        ["{bad}", "--project", "ZeroAlloc.Jev"],
    ],
)
def test_a_wrong_command_line_exits_with_2(tmp_path, args):
    good = write(tmp_path, "dotnet-box.json", dotnet_file())
    bad = write(tmp_path, "bad.json", {"machine": machine(), "results": [{"client": "x"}]})
    argv = [a.format(file=good, bad=bad) for a in args]

    run = subprocess.run([sys.executable, MERGE, *argv], capture_output=True, text=True, check=False)

    assert run.returncode == 2
    assert "Usage:" in run.stderr
