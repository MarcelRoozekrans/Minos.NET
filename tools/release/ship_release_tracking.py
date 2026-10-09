#!/usr/bin/env python3
"""Mark everything in the Unshipped release-tracking files as shipped in a release.

Usage, from the root of the repository whose files to move:
    python3 ship-release-tracking.py <version>
    python3 ship-release-tracking.py --check

Two sets of files track what a release contains, and both are only useful when
the Unshipped half is moved into the Shipped half as each version goes out:

* src/*/AnalyzerReleases.{Shipped,Unshipped}.md
  The Roslyn release-tracking analyzers read these. Once a rule is in a
  "## Release x.y.z" section, changing its severity or category, or removing
  it, has to be declared, so an accidental change to a shipped rule fails the build.
* src/*/PublicAPI.{Shipped,Unshipped}*.txt
  The public-API analyzers read these. Entries in Shipped are the API a
  release has promised; *REMOVED* entries in Unshipped delete their Shipped line.
  A suffixed pair such as PublicAPI.Unshipped.modern.txt, used for API that only
  some target frameworks compile, moves into its own PublicAPI.Shipped.modern.txt.

Both are discovered rather than listed, so a new analyzer project or package is
covered without editing this script.

The ship-release-tracking reusable workflow runs this on the release PR branch
with the version that PR releases, so the release commit carries the move. The
script is idempotent: with nothing unshipped it changes no file.

--check changes nothing. It lists every Unshipped entry and exits 1 if there is
any. The release-tracking reusable workflow runs it on release PRs, so a release
cannot merge with entries unmoved.

This is the shared copy in ZeroAlloc-Net/.github. It started as
scripts/ship-release-tracking.py in ZeroAlloc.ORM, which proved it on the 2.0.1
release; see ZeroAlloc-Net/.github#38.
"""
# Vendored from ZeroAlloc-Net/.github scripts/ship-release-tracking.py at 607a5d3 (same author), so this repository depends on no org workflow.

from __future__ import annotations

import glob
import os
import re
import sys

ANALYZER_HEADER = (
    "; {kind} analyzer release{s}.\n"
    "; https://github.com/dotnet/roslyn-analyzers/blob/main/src/"
    "Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md\n"
)
RULE_ROW = re.compile(r"^[A-Za-z]+\d+\s*\|")
REMOVED_PREFIX = "*REMOVED*"
UNSHIPPED_API = re.compile(r"^PublicAPI\.Unshipped(?P<suffix>(\.[^.]+)*)\.txt$")


def read(path: str) -> str:
    with open(path, encoding="utf-8-sig") as f:
        return f.read().replace("\r\n", "\n")


def write(path: str, text: str) -> None:
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        f.write(text)


def analyzer_unshipped_paths() -> list[str]:
    return sorted(glob.glob(os.path.join("src", "*", "AnalyzerReleases.Unshipped.md")))


def api_unshipped_paths() -> list[str]:
    return sorted(p for p in glob.glob(os.path.join("src", "*", "PublicAPI.Unshipped*.txt"))
                  if UNSHIPPED_API.match(os.path.basename(p)))


def counterpart(unshipped_path: str, shipped_name: str) -> str:
    shipped_path = os.path.join(os.path.dirname(unshipped_path), shipped_name)
    if not os.path.exists(shipped_path):
        sys.exit(f"{unshipped_path} has no {shipped_name} beside it; add an empty one "
                 f"and register it with the analyzer.")
    return shipped_path


def ship_analyzer_rules(unshipped_path: str, version: str) -> bool:
    unshipped = read(unshipped_path)
    if not any(RULE_ROW.match(line) for line in unshipped.splitlines()):
        return False

    shipped_path = counterpart(unshipped_path, "AnalyzerReleases.Shipped.md")
    shipped = read(shipped_path).rstrip("\n")
    if re.search(rf"^## Release {re.escape(version)}\s*$", shipped, re.MULTILINE):
        sys.exit(f"{shipped_path} already has a Release {version} section while "
                 f"{unshipped_path} still lists rules; resolve by hand.")
    if not shipped:
        shipped = ANALYZER_HEADER.format(kind="Shipped", s="s").rstrip("\n")

    # Keep the section headings and tables; drop the leading ';' comment lines.
    body = "\n".join(
        line for line in unshipped.splitlines() if not line.startswith(";")
    ).strip("\n")
    write(shipped_path, shipped + f"\n\n## Release {version}\n\n{body}\n")
    write(unshipped_path, ANALYZER_HEADER.format(kind="Unshipped", s=""))
    return True


def api_entries(text: str) -> list[str]:
    return [line for line in text.splitlines() if line.strip() and not line.startswith("#")]


def ship_public_api(unshipped_path: str) -> bool:
    unshipped_text = read(unshipped_path)
    pending = api_entries(unshipped_text)
    if not pending:
        return False

    suffix = UNSHIPPED_API.match(os.path.basename(unshipped_path)).group("suffix")
    shipped_path = counterpart(unshipped_path, f"PublicAPI.Shipped{suffix}.txt")
    shipped_text = read(shipped_path)
    directives = [line for line in shipped_text.splitlines() if line.startswith("#")]
    entries = set(api_entries(shipped_text))
    for entry in pending:
        if entry.startswith(REMOVED_PREFIX):
            removed = entry[len(REMOVED_PREFIX):]
            if removed not in entries:
                sys.exit(f"{unshipped_path}: {entry!r} is not in {shipped_path}.")
            entries.discard(removed)
        else:
            entries.add(entry)

    # Same order the files already use and the analyzer's code fix produces.
    ordered = sorted(entries, key=lambda s: (s.lower(), s))
    write(shipped_path, "\n".join(directives + ordered) + "\n")
    unshipped_directives = [line for line in unshipped_text.splitlines() if line.startswith("#")]
    write(unshipped_path, "\n".join(unshipped_directives) + "\n")
    return True


def unshipped_entries() -> list[str]:
    found = []
    for path in analyzer_unshipped_paths():
        found += [f"{path}: {line}" for line in read(path).splitlines() if RULE_ROW.match(line)]
    for path in api_unshipped_paths():
        found += [f"{path}: {line}" for line in api_entries(read(path))]
    return found


def main() -> None:
    if sys.argv[1:] == ["--check"]:
        if not analyzer_unshipped_paths() and not api_unshipped_paths():
            sys.exit("no src/*/AnalyzerReleases.Unshipped.md or src/*/PublicAPI.Unshipped*.txt "
                     "found; run this from the repository root.")
        found = unshipped_entries()
        if found:
            print("Unshipped entries remain on a release PR; run "
                  "ship-release-tracking.py <version> on the branch:")
            print("\n".join(found))
            sys.exit(1)
        print("nothing unshipped")
        return
    if len(sys.argv) != 2 or not re.fullmatch(r"\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?", sys.argv[1]):
        sys.exit("usage: ship-release-tracking.py <version>, for example 2.0.1")
    version = sys.argv[1]
    # Resolve every Shipped counterpart before writing anything, so a missing file
    # fails the run with no half-moved state left behind.
    for path in analyzer_unshipped_paths():
        counterpart(path, "AnalyzerReleases.Shipped.md")
    for path in api_unshipped_paths():
        suffix = UNSHIPPED_API.match(os.path.basename(path)).group("suffix")
        counterpart(path, f"PublicAPI.Shipped{suffix}.txt")
    changed = [f"{p} -> Release {version}" for p in analyzer_unshipped_paths()
               if ship_analyzer_rules(p, version)]
    changed += [p for p in api_unshipped_paths() if ship_public_api(p)]
    print("\n".join(changed) if changed else "nothing unshipped")


if __name__ == "__main__":
    main()
