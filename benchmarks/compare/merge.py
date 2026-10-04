"""Merges the comparison harnesses' result files into one Markdown table.

Usage: python merge.py results/<machine>/*.json --project <name> [--run-url <url>] [--commit <sha>] [--save <path>]
       python merge.py --across <run.json> [<run.json> ...] --project <name>

It prints one row per client, fastest throughput first, then the machine line, the order line when the .NET harness
recorded one, the run line and any client notes as footnotes. Every file must come from one run on one machine. The
project's own rows, those whose library is the project, are in bold. A .NET result must have its bytes per call; only
the other runtimes may leave them null.

An input is either a harness's result file, with a 'machine' object and a 'results' list, or a published run that
--save wrote: a 'run' object and a 'files' list of the harness files, each kept whole under its file name. --run-url and
--commit name the run, overriding what a published run says; --save writes the inputs as one published run.

--across reads several published runs, one per input, and prints the runs table, one row per run with its CPU and mock
ceiling, then per client the lowest and highest mean latency, throughput, share of the mock ceiling and bytes per call
over those runs. Every run must hold the same clients.
"""

import argparse
import json
import os
import sys

USAGE = (
    "Usage: python merge.py <result.json> [<result.json> ...] --project <name>"
    " [--run-url <url>] [--commit <sha>] [--save <path>]\n"
    "       python merge.py --across <run.json> [<run.json> ...] --project <name>"
)
DASH = "—"


class MergeError(Exception):
    """A result file is missing, malformed or from another run."""


class _Parser(argparse.ArgumentParser):
    def error(self, message):
        raise MergeError(message)


def _is_result_file(data):
    return isinstance(data, dict) and isinstance(data.get("machine"), dict) and isinstance(data.get("results"), list)


def _entries(paths):
    """Reads each input and returns its harness files as dicts of name, label, machine and results, plus each run."""
    entries = []
    runs = []
    for path in paths:
        try:
            with open(path, encoding="utf-8") as f:
                data = json.load(f)
        except (OSError, ValueError) as error:
            raise MergeError(f"{path}: {error}") from error
        if isinstance(data, dict) and "files" in data:
            run = data.get("run")
            if not isinstance(data["files"], list) or not data["files"] or (run is not None and not isinstance(run, dict)):
                raise MergeError(f"{path}: not a published run; it needs a non-empty 'files' list and a 'run' object.")
            runs.append(run or {})
            for entry in data["files"]:
                if not _is_result_file(entry) or not isinstance(entry.get("name"), str):
                    raise MergeError(f"{path}: each entry in 'files' needs a 'name', a 'machine' object and a 'results' list.")
                entries.append({"name": entry["name"], "label": f"{path}:{entry['name']}", "data": entry})
        elif _is_result_file(data):
            entries.append({"name": os.path.basename(path), "label": path, "data": data})
        else:
            raise MergeError(f"{path}: not a result file; it needs a 'machine' object and a 'results' list.")
    if not entries:
        raise MergeError("No result files given.")
    return entries, runs


def load(paths):
    """Reads each result file and returns (path, machine, results) triples; a published run gives one per harness file."""
    entries, _ = _entries(paths)
    return [(e["label"], e["data"]["machine"], e["data"]["results"]) for e in entries]


def load_orders(paths):
    """The order each harness file recorded, where it recorded one: the .NET harness's interleaving and rotation."""
    entries, _ = _entries(paths)
    return [e["data"]["order"] for e in entries if e["data"].get("order") is not None]


def load_run(paths, url=None, commit=None):
    """The run the inputs name, as a dict with 'url' and 'commit' where known; the arguments override the inputs."""
    _, runs = _entries(paths)
    run = {}
    for key, given in (("url", url), ("commit", commit)):
        value = given if given is not None else _one([r.get(key) for r in runs], f"the run's {key}")
        if value is not None:
            run[key] = value
    return run


def save(paths, path, run):
    """Writes the inputs as one published run: the run, then every harness file whole, under its file name."""
    entries, _ = _entries(paths)
    files = []
    for e in entries:
        whole = {"name": e["name"]}
        whole.update({key: value for key, value in e["data"].items() if key != "name"})
        files.append(whole)
    data = {"run": run, "files": files}
    try:
        with open(path, "w", encoding="utf-8", newline="\n") as f:
            json.dump(data, f, indent=2, ensure_ascii=False)
            f.write("\n")
    except OSError as error:
        raise MergeError(f"{path}: {error}") from error


def _ms(value):
    return f"{value:.3f}"


def _whole(value):
    return f"{value:,.0f}"


def _cell(text):
    return str(text).replace("|", "\\|")


def _one(values, what):
    """The single distinct non-null value, or None; more than one means the files are from different runs."""
    distinct = sorted({v for v in values if v is not None})
    if len(distinct) > 1:
        raise MergeError(f"The files disagree on {what}: {', '.join(map(str, distinct))}. Merge one run at a time.")
    return distinct[0] if distinct else None


def _client_cores(files):
    """The clients' cores: one list when every harness agrees, else each harness's runtime with its own."""
    by_cores = {}
    for _, machine, results in files:
        runtimes = sorted({r.get("runtime", "?") for r in results})
        by_cores.setdefault(machine.get("cores"), []).extend(runtimes)
    if len(by_cores) == 1:
        cores = next(iter(by_cores))
        return cores if cores is not None else "unpinned"
    parts = []
    for cores, runtimes in sorted(by_cores.items(), key=lambda item: (item[0] is None, item[0] or "")):
        parts.append(f"{cores if cores is not None else 'unpinned'} ({', '.join(sorted(set(runtimes)))})")
    return "; ".join(parts)


def _run_line(run):
    """'Run: [run <id>](<url>), commit `<short sha>`.', with whichever of the two the run has."""
    parts = []
    if run.get("url"):
        url = run["url"]
        parts.append(f"[run {url.rstrip('/').rsplit('/', 1)[-1]}]({url})")
    if run.get("commit"):
        parts.append(f"commit `{run['commit'][:7]}`")
    return f"Run: {', '.join(parts)}." if parts else None


def _order_line(orders):
    """'Order: ...' from the .NET harness's recorded order, or None when no harness recorded one."""
    if not orders:
        return None
    if len(orders) > 1:
        raise MergeError("More than one harness file recorded an order; merge one run at a time.")
    order = orders[0]
    rounds, calls = order["latencyRounds"], order["callsPerRound"]
    return (
        f"Order: latency in {rounds} interleaved rounds of {calls} calls per .NET client, rotated by round from "
        f"rotation {order['rotation']}; throughput {', '.join(order['throughput'])}."
    )


def _check_bytes(results):
    """A .NET result must carry its bytes per call; only the other runtimes have no such figure."""
    for r in results:
        if r.get("runtime") == ".NET" and r.get("allocatedBytesPerCall") is None:
            raise MergeError(f"{r['client']}: a .NET result has no allocatedBytesPerCall; the .NET harness always measures it.")


def render(files, project, run=None, orders=None):
    """Renders the table, the machine line, the order line, the run line and the footnotes as Markdown text."""
    name = _one([m.get("name") for _, m, _ in files], "the machine name")
    ceiling = _one([m.get("mockCeilingPerSecond") for _, m, _ in files], "the mock ceiling")
    mock_cores = _one([m.get("mockCores") for _, m, _ in files], "the mock's cores")
    # The file with the ceiling is the .NET harness's; its OS and CPU strings come from .NET and BenchmarkDotNet.
    primary = next((m for _, m, _ in files if m.get("mockCeilingPerSecond") is not None), files[0][1])
    date = max((m.get("date") for _, m, _ in files if m.get("date")), default=None)

    results = [r for _, _, rs in files for r in rs]
    if not results:
        raise MergeError("The result files hold no results.")
    _check_bytes(results)
    results.sort(key=lambda r: r["throughputPerSecond"], reverse=True)

    notes = []
    lines = [
        f"### {project}: client comparison",
        "",
        "| Client | Library | Runtime | Mean (ms) | p50 (ms) | p99 (ms) | Throughput (/s) | Of mock ceiling | Bytes/call |",
        "|---|---|---|--:|--:|--:|--:|--:|--:|",
    ]
    for r in results:
        client = _cell(r["client"])
        if r.get("library") == project:
            client = f"**{client}**"
        note = r.get("note")
        if note:
            if note not in notes:
                notes.append(note)
            client += f"[^{notes.index(note) + 1}]"
        latency = r["latencyMs"]
        throughput = r["throughputPerSecond"]
        share = f"{throughput / ceiling * 100:.0f}%" if ceiling else DASH
        allocated = r.get("allocatedBytesPerCall")
        lines.append(
            "| "
            + " | ".join(
                [
                    client,
                    _cell(f"{r['library']} {r['version']}"),
                    _cell(f"{r['runtime']} {r['runtimeVersion']}"),
                    _ms(latency["mean"]),
                    _ms(latency["p50"]),
                    _ms(latency["p99"]),
                    _whole(throughput),
                    share,
                    _whole(allocated) if allocated is not None else DASH,
                ]
            )
            + " |"
        )

    machine = [
        f"Machine: {name if name is not None else DASH}",
        f"OS: {primary.get('os') or DASH}",
        f"CPU: {primary.get('cpu') or DASH}",
        f"date: {date or DASH}",
        f"mock cores: {mock_cores if mock_cores is not None else 'unpinned'}",
        f"client cores: {_client_cores(files)}",
        f"mock ceiling: {_whole(ceiling) + '/s' if ceiling is not None else DASH}",
    ]
    lines += ["", "; ".join(machine) + "."]
    order_line = _order_line(orders or [])
    if order_line:
        lines += ["", order_line]
    run_line = _run_line(run or {})
    if run_line:
        lines += ["", run_line]
    if notes:
        lines.append("")
        lines += [f"[^{i}]: {note}" for i, note in enumerate(notes, start=1)]
    return "\n".join(lines) + "\n"


def _span(values, format_value):
    """'low to high' over the values, or the one value when they are all equal; a dash when there are none."""
    present = [v for v in values if v is not None]
    if not present:
        return DASH
    if len(present) != len(values):
        raise MergeError("A client has a figure in some runs and none in others.")
    low, high = format_value(min(present)), format_value(max(present))
    return low if low == high else f"{low} to {high}"


def render_across(runs, project):
    """Renders the runs table and the per-client lowest and highest figures over several published runs."""
    if len(runs) < 1:
        raise MergeError("--across needs at least one published run.")
    lines = [
        f"### {project}: across runs",
        "",
        "| Run | Commit | CPU | Mock ceiling (/s) |",
        "|---|---|---|--:|",
    ]
    by_client = {}
    clients = None
    for label, files, run in runs:
        ceiling = _one([m.get("mockCeilingPerSecond") for _, m, _ in files], "the mock ceiling")
        primary = next((m for _, m, _ in files if m.get("mockCeilingPerSecond") is not None), files[0][1])
        url = run.get("url")
        name = f"[run {url.rstrip('/').rsplit('/', 1)[-1]}]({url})" if url else _cell(label)
        commit = f"`{run['commit'][:7]}`" if run.get("commit") else DASH
        lines.append(
            f"| {name} | {commit} | {_cell(primary.get('cpu') or DASH)} | {_whole(ceiling) if ceiling is not None else DASH} |"
        )
        results = [r for _, _, rs in files for r in rs]
        _check_bytes(results)
        names = sorted(r["client"] for r in results)
        if clients is None:
            clients = names
        elif names != clients:
            raise MergeError(f"{label}: its clients differ from the first run's; every run must hold the same clients.")
        for r in results:
            figures = by_client.setdefault(r["client"], {"library": r.get("library"), "rows": []})
            share = r["throughputPerSecond"] / ceiling * 100 if ceiling else None
            figures["rows"].append((r["latencyMs"]["mean"], r["throughputPerSecond"], share, r.get("allocatedBytesPerCall")))

    lines += [
        "",
        "| Client | Mean (ms) | Throughput (/s) | Of mock ceiling | Bytes/call |",
        "|---|--:|--:|--:|--:|",
    ]
    ordered = sorted(by_client.items(), key=lambda item: (-min(row[1] for row in item[1]["rows"]), item[0]))
    for client, figures in ordered:
        rows = figures["rows"]
        name = _cell(client)
        if figures["library"] == project:
            name = f"**{name}**"
        lines.append(
            "| "
            + " | ".join(
                [
                    name,
                    _span([row[0] for row in rows], _ms),
                    _span([row[1] for row in rows], _whole),
                    _span([row[2] for row in rows], lambda v: f"{v:.0f}%"),
                    _span([row[3] for row in rows], _whole),
                ]
            )
            + " |"
        )
    lines += ["", f"Runs: {len(runs)}. Each range is the lowest to the highest figure over the runs."]
    return "\n".join(lines) + "\n"


def load_across(paths):
    """Each input as one run: (label, its harness files as load() gives them, its run object)."""
    return [(path, load([path]), load_run([path])) for path in paths]


def main(argv):
    parser = _Parser(add_help=False, allow_abbrev=False)
    parser.add_argument("files", nargs="+")
    parser.add_argument("--project", required=True)
    parser.add_argument("--run-url")
    parser.add_argument("--commit")
    parser.add_argument("--save")
    parser.add_argument("--across", action="store_true")
    try:
        opts = parser.parse_args(argv)
        if not opts.project.strip():
            raise MergeError("--project must not be blank.")
        if opts.across and (opts.run_url or opts.commit or opts.save):
            raise MergeError("--across takes no --run-url, --commit or --save; each published run names itself.")
        try:
            if opts.across:
                table = render_across(load_across(opts.files), opts.project.strip())
            else:
                run = load_run(opts.files, opts.run_url, opts.commit)
                table = render(load(opts.files), opts.project.strip(), run, load_orders(opts.files))
                if opts.save:
                    save(opts.files, opts.save, run)
        except (KeyError, TypeError) as error:
            raise MergeError(f"A result entry is missing a field or has the wrong type: {error!r}") from error
        # UTF-8 with LF on every OS: the dash for a missing value survives a redirect, and the output is the same everywhere.
        sys.stdout.reconfigure(encoding="utf-8", newline="\n")
        sys.stdout.write(table)
    except MergeError as error:
        print(str(error), file=sys.stderr)
        print(USAGE, file=sys.stderr)
        return 2
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
