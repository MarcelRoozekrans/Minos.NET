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
            result("jev", "Minos.NET", ".NET", "10.0.0", 50000.0, 1024),
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
    text = merge.render(merge.load(files), "Minos.NET")

    assert table_rows(text) == [
        "| raw | HttpClient, System.Text.Json 1.2.3 | .NET 10.0.0 | 0.250 | 0.200 | 1.500 | 60,000 | 60% | 800 |",
        "| **jev** | Minos.NET 1.2.3 | .NET 10.0.0 | 0.250 | 0.200 | 1.500 | 50,000 | 50% | 1,024 |",
        "| jevsharp[^1] | JevSharp 1.2.3 | .NET 10.0.0 | 0.250 | 0.200 | 1.500 | 40,000 | 40% | 4,096 |",
        "| typesafe-ai-sdk-js | @typesafe-ai/sdk 1.2.3 | Node.js 24.1.0 | 0.500 | 0.200 | 1.500 | 4,940 | 5% | — |",
    ]


def test_the_header_names_the_project_and_every_column(files):
    lines = merge.render(merge.load(files), "Minos.NET").splitlines()

    assert lines[0] == "### Minos.NET: client comparison"
    assert lines[2] == (
        "| Client | Library | Runtime | Mean (ms) | p50 (ms) | p99 (ms) | Throughput (/s) | Of mock ceiling | Bytes/call |"
    )


def test_the_machine_line_and_the_footnotes_follow_the_table(files):
    text = merge.render(merge.load(files), "Minos.NET")

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

    text = merge.render(merge.load(files), "Minos.NET")

    assert "client cores: 0-11 (.NET); unpinned (Node.js);" in text


def test_without_a_ceiling_the_share_is_a_dash(tmp_path):
    text = merge.render(merge.load([write(tmp_path, "js-box.json", js_file())]), "Minos.NET")

    assert table_rows(text)[0].split(" | ")[7] == "—"
    assert "mock ceiling: —." in text


def test_files_from_two_machines_are_refused(tmp_path):
    js = js_file()
    js["machine"]["name"] = "other"
    files = [write(tmp_path, "dotnet-box.json", dotnet_file()), write(tmp_path, "js-other.json", js)]

    with pytest.raises(merge.MergeError, match="machine name"):
        merge.render(merge.load(files), "Minos.NET")


def test_files_with_two_mock_core_sets_are_refused(tmp_path):
    js = js_file()
    js["machine"]["mockCores"] = "0-3"
    files = [write(tmp_path, "dotnet-box.json", dotnet_file()), write(tmp_path, "js-box.json", js)]

    with pytest.raises(merge.MergeError, match="mock's cores"):
        merge.render(merge.load(files), "Minos.NET")


def test_the_command_line_prints_the_table(files):
    run = subprocess.run(
        [sys.executable, MERGE, *files, "--project", "Minos.NET"], capture_output=True, check=False
    )

    assert run.returncode == 0, run.stderr
    out = run.stdout.decode("utf-8")
    assert out.startswith("### Minos.NET: client comparison\n")
    assert "| typesafe-ai-sdk-js |" in out and "— |" in out


@pytest.mark.parametrize(
    "args",
    [
        ["--project", "Minos.NET"],
        ["missing.json", "--project", "Minos.NET"],
        ["{file}"],
        ["{file}", "--project", " "],
        ["{bad}", "--project", "Minos.NET"],
    ],
)
def test_a_wrong_command_line_exits_with_2(tmp_path, args):
    good = write(tmp_path, "dotnet-box.json", dotnet_file())
    bad = write(tmp_path, "bad.json", {"machine": machine(), "results": [{"client": "x"}]})
    argv = [a.format(file=good, bad=bad) for a in args]

    run = subprocess.run([sys.executable, MERGE, *argv], capture_output=True, text=True, check=False)

    assert run.returncode == 2
    assert "Usage:" in run.stderr


RUN_URL = "https://github.com/o/r/actions/runs/123"


def test_the_run_line_names_the_run_and_the_short_commit(files):
    text = merge.render(merge.load(files), "Minos.NET", {"url": RUN_URL, "commit": "bf1ef0b9a521470cf"})

    lines = text.splitlines()
    run = lines.index(f"Run: [run 123]({RUN_URL}), commit `bf1ef0b`.")
    assert lines[run - 2].startswith("Machine: box;")
    assert lines[run + 2] == "[^1]: Sends no auth header."


def test_without_a_run_there_is_no_run_line(files):
    assert "Run:" not in merge.render(merge.load(files), "Minos.NET")
    assert "Run:" not in merge.render(merge.load(files), "Minos.NET", {})


def test_a_saved_run_keeps_every_file_whole_and_renders_the_same_table(files, tmp_path):
    saved = str(tmp_path / "ci.json")
    run = {"url": RUN_URL, "commit": "abc1234def"}

    merge.save(files, saved, run)

    with open(saved, encoding="utf-8") as f:
        data = json.load(f)
    assert data["run"] == run
    assert [entry["name"] for entry in data["files"]] == ["dotnet-box.json", "js-box.json"]
    assert {k: data["files"][0][k] for k in ("machine", "results")} == dotnet_file()
    assert {k: data["files"][1][k] for k in ("machine", "results")} == js_file()
    assert merge.load_run([saved]) == run
    assert merge.render(merge.load([saved]), "Minos.NET", merge.load_run([saved])) == merge.render(
        merge.load(files), "Minos.NET", run
    )


def test_the_arguments_override_the_saved_run(files, tmp_path):
    saved = str(tmp_path / "ci.json")
    merge.save(files, saved, {"url": RUN_URL, "commit": "abc1234"})

    assert merge.load_run([saved], commit="fff0000") == {"url": RUN_URL, "commit": "fff0000"}


def test_two_saved_runs_that_name_different_runs_are_refused(files, tmp_path):
    first, second = str(tmp_path / "a.json"), str(tmp_path / "b.json")
    merge.save(files, first, {"url": RUN_URL})
    merge.save(files, second, {"url": RUN_URL + "4"})

    with pytest.raises(merge.MergeError, match="run's url"):
        merge.load_run([first, second])


def test_the_command_line_saves_a_run_and_reads_it_back(files, tmp_path):
    saved = str(tmp_path / "ci.json")
    first = subprocess.run(
        [sys.executable, MERGE, *files, "--project", "Minos.NET", "--run-url", RUN_URL, "--commit", "abc1234", "--save", saved],
        capture_output=True,
        check=False,
    )
    second = subprocess.run([sys.executable, MERGE, saved, "--project", "Minos.NET"], capture_output=True, check=False)

    assert first.returncode == 0, first.stderr
    assert second.returncode == 0, second.stderr
    assert second.stdout == first.stdout
    assert f"Run: [run 123]({RUN_URL}), commit `abc1234`.".encode() in second.stdout


@pytest.mark.parametrize(
    "content",
    [
        {"run": {}, "files": []},
        {"run": {}, "files": "dotnet-box.json"},
        {"run": "123", "files": [{"name": "x.json", "machine": {}, "results": []}]},
        {"run": {}, "files": [{"machine": {}, "results": []}]},
        {"run": {}, "files": [{"name": "x.json", "results": []}]},
    ],
)
def test_a_malformed_saved_run_exits_with_2(tmp_path, content):
    bad = write(tmp_path, "ci.json", content)

    run = subprocess.run([sys.executable, MERGE, bad, "--project", "Minos.NET"], capture_output=True, text=True, check=False)

    assert run.returncode == 2
    assert "Usage:" in run.stderr


ORDER = {"latencyRounds": 20, "callsPerRound": 100, "rotation": 2, "throughput": ["jevsharp", "raw", "jev"]}


def test_a_dotnet_result_without_bytes_is_refused(tmp_path):
    dotnet = dotnet_file()
    dotnet["results"][0]["allocatedBytesPerCall"] = None

    with pytest.raises(merge.MergeError, match="jev: a .NET result has no allocatedBytesPerCall"):
        merge.render(merge.load([write(tmp_path, "dotnet-box.json", dotnet)]), "Minos.NET")


def test_the_order_line_follows_the_machine_line_and_a_saved_run_keeps_it(tmp_path):
    dotnet = dotnet_file()
    dotnet["order"] = ORDER
    files = [write(tmp_path, "dotnet-box.json", dotnet), write(tmp_path, "js-box.json", js_file())]
    saved = str(tmp_path / "ci.json")

    text = merge.render(merge.load(files), "Minos.NET", None, merge.load_orders(files))
    merge.save(files, saved, {})

    lines = text.splitlines()
    order = lines.index(
        "Order: latency in 20 interleaved rounds of 100 calls per .NET client, rotated by round from rotation 2; "
        "throughput jevsharp, raw, jev."
    )
    assert lines[order - 2].startswith("Machine: box;")
    with open(saved, encoding="utf-8") as f:
        assert json.load(f)["files"][0]["order"] == ORDER
    assert merge.render(merge.load([saved]), "Minos.NET", None, merge.load_orders([saved])) == text


def test_without_an_order_there_is_no_order_line(files):
    assert "Order:" not in merge.render(merge.load(files), "Minos.NET", None, merge.load_orders(files))


def saved_run(tmp_path, name, url, scale, ceiling, cpu="AMD EPYC 7763"):
    """A published run whose throughputs are the sample's times scale, and whose mean latencies are scale ms."""
    dotnet, js = dotnet_file(), js_file()
    dotnet["machine"].update(mockCeilingPerSecond=ceiling, cpu=cpu)
    for r in dotnet["results"] + js["results"]:
        r["throughputPerSecond"] *= scale
        r["latencyMs"]["mean"] = scale
    files = [write(tmp_path, f"{name}-dotnet.json", dotnet), write(tmp_path, f"{name}-js.json", js)]
    path = str(tmp_path / f"{name}.json")
    merge.save(files, path, {"url": url, "commit": f"{name}000000abc"})
    return path


def test_across_runs_gives_each_runs_ceiling_and_each_clients_lowest_and_highest(tmp_path):
    runs = [
        saved_run(tmp_path, "aaa", RUN_URL, 1.0, 100000.0),
        saved_run(tmp_path, "bbb", RUN_URL + "4", 2.0, 150000.0, cpu="AMD EPYC 9V74"),
    ]

    text = merge.render_across(merge.load_across(runs), "Minos.NET")

    assert text.splitlines()[0] == "### Minos.NET: across runs"
    assert f"| [run 123]({RUN_URL}) | `aaa0000` | AMD EPYC 7763 | 100,000 |" in text.splitlines()
    assert f"| [run 1234]({RUN_URL}4) | `bbb0000` | AMD EPYC 9V74 | 150,000 |" in text.splitlines()
    rows = [r for r in table_rows(text) if not r.startswith(("| [run", "| Run"))]
    assert rows == [
        "| raw | 1.000 to 2.000 | 60,000 to 120,000 | 60% to 80% | 800 |",
        "| **jev** | 1.000 to 2.000 | 50,000 to 100,000 | 50% to 67% | 1,024 |",
        "| jevsharp | 1.000 to 2.000 | 40,000 to 80,001 | 40% to 53% | 4,096 |",
        "| typesafe-ai-sdk-js | 1.000 to 2.000 | 4,940 to 9,880 | 5% to 7% | — |",
    ]
    assert text.endswith("Runs: 2. Each range is the lowest to the highest figure over the runs.\n")


def test_across_one_run_gives_single_figures(tmp_path):
    text = merge.render_across(merge.load_across([saved_run(tmp_path, "aaa", RUN_URL, 1.0, 100000.0)]), "Minos.NET")

    assert "| raw | 1.000 | 60,000 | 60% | 800 |" in text.splitlines()


def test_across_runs_with_different_clients_is_refused(tmp_path):
    first = saved_run(tmp_path, "aaa", RUN_URL, 1.0, 100000.0)
    dotnet = dotnet_file()
    dotnet["machine"]["mockCeilingPerSecond"] = 1.0
    second = str(tmp_path / "bbb.json")
    merge.save([write(tmp_path, "bbb-dotnet.json", dotnet)], second, {"url": RUN_URL + "4"})

    with pytest.raises(merge.MergeError, match="clients differ"):
        merge.render_across(merge.load_across([first, second]), "Minos.NET")


def test_the_command_line_prints_the_across_runs_table(tmp_path):
    runs = [saved_run(tmp_path, "aaa", RUN_URL, 1.0, 100000.0), saved_run(tmp_path, "bbb", RUN_URL + "4", 2.0, 150000.0)]

    result = subprocess.run([sys.executable, MERGE, "--across", *runs, "--project", "Minos.NET"], capture_output=True, check=False)
    refused = subprocess.run(
        [sys.executable, MERGE, "--across", *runs, "--project", "Minos.NET", "--save", str(tmp_path / "x.json")],
        capture_output=True,
        text=True,
        check=False,
    )

    assert result.returncode == 0, result.stderr
    assert result.stdout.decode("utf-8") == merge.render_across(merge.load_across(runs), "Minos.NET")
    assert refused.returncode == 2
    assert "--across takes no" in refused.stderr
