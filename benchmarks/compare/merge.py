"""Merges the comparison harnesses' result files into one Markdown table.

Usage: python merge.py results/<machine>/*.json --project <name>

It prints one row per client, fastest throughput first, then the machine line and any client notes as footnotes. Every
file must come from one run on one machine. The project's own rows, those whose library is the project, are in bold.
"""

import argparse
import json
import sys

USAGE = "Usage: python merge.py <result.json> [<result.json> ...] --project <name>"
DASH = "—"


class MergeError(Exception):
    """A result file is missing, malformed or from another run."""


class _Parser(argparse.ArgumentParser):
    def error(self, message):
        raise MergeError(message)


def load(paths):
    """Reads each result file and returns (path, machine, results) triples."""
    files = []
    for path in paths:
        try:
            with open(path, encoding="utf-8") as f:
                data = json.load(f)
        except (OSError, ValueError) as error:
            raise MergeError(f"{path}: {error}") from error
        if not isinstance(data, dict) or not isinstance(data.get("machine"), dict) or not isinstance(data.get("results"), list):
            raise MergeError(f"{path}: not a result file; it needs a 'machine' object and a 'results' list.")
        files.append((path, data["machine"], data["results"]))
    if not files:
        raise MergeError("No result files given.")
    return files


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


def render(files, project):
    """Renders the table, the machine line and the footnotes as Markdown text."""
    name = _one([m.get("name") for _, m, _ in files], "the machine name")
    ceiling = _one([m.get("mockCeilingPerSecond") for _, m, _ in files], "the mock ceiling")
    mock_cores = _one([m.get("mockCores") for _, m, _ in files], "the mock's cores")
    # The file with the ceiling is the .NET harness's; its OS and CPU strings come from .NET and BenchmarkDotNet.
    primary = next((m for _, m, _ in files if m.get("mockCeilingPerSecond") is not None), files[0][1])
    date = max((m.get("date") for _, m, _ in files if m.get("date")), default=None)

    results = [r for _, _, rs in files for r in rs]
    if not results:
        raise MergeError("The result files hold no results.")
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
    if notes:
        lines.append("")
        lines += [f"[^{i}]: {note}" for i, note in enumerate(notes, start=1)]
    return "\n".join(lines) + "\n"


def main(argv):
    parser = _Parser(add_help=False, allow_abbrev=False)
    parser.add_argument("files", nargs="+")
    parser.add_argument("--project", required=True)
    try:
        opts = parser.parse_args(argv)
        if not opts.project.strip():
            raise MergeError("--project must not be blank.")
        try:
            table = render(load(opts.files), opts.project.strip())
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
