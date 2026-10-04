"""Splits the CPU cores between the benchmark mock and the clients, for run.ps1 and run.sh.

Usage: python cores.py [--count <n>] [--mock-cores <list>] [--client-cores <list>] [--ranks <list>]
                       [--siblings <groups>] [--sysfs <dir>]

It prints key=value lines that both runners read:

    mock=12-19
    clients=0-11
    clientmask=4095
    clientmask64=4095
    split=hybrid: the fastest cores for the clients, the others for the mock
    siblings=0-1;2-3;4-5;6-7;8-9;10-11;12;13;14;15;16;17;18;19

- --mock-cores and --client-cores, lists such as 12-19 or 0-3,8, override the split. Given one, the other set is every
  remaining core. An override that puts two threads of one physical core on different sides is used as given, with a
  warning on stderr.
- Otherwise the split works in physical cores, so the sibling threads of one core always land on the same side and
  the mock never runs on a sibling of a client thread. On a CPU with more than one core type, the clients get the
  physical cores of the fastest type and the mock the others. Without types, or with one, the mock gets the lower half
  of the physical cores, in order of their first logical core, and the clients the upper half.
- The physical cores come from --siblings, groups of logical cores separated by ';', such as 0,1;2,3: run.ps1 passes
  each core's logical processors from GetLogicalProcessorInformationEx's RelationProcessorCore. Without --siblings they
  are read from sysfs, devices/system/cpu/cpu<n>/topology/thread_siblings_list. When neither says, every logical core
  counts as its own physical core and siblings= is 'unknown': on an SMT machine the two sides may then share a core.
- Pinning keeps the mock's threads and the clients' threads off each other's cores. It does not separate what the
  cores share, such as the last-level cache and the memory bandwidth.
- Core types come from --ranks, one number per logical core in index order, where higher is faster: run.ps1 passes
  each core's EfficiencyClass from GetLogicalProcessorInformationEx. Without --ranks they are read from sysfs: Intel's
  devices/cpu_core and devices/cpu_atom first, then each core's devices/system/cpu/cpu<n>/cpu_capacity.
- clientmask is the clients' affinity mask, and clientmask64 the same bits as a signed 64-bit number, the form
  .NET's ProcessorAffinity takes, so core 63 does not overflow.

A wrong option or an impossible split prints the reason and exits with 2.
"""

import argparse
import os
import re
import sys

MAX_CORES = 64
LIST = re.compile(r"\d+(-\d+)?(,\d+(-\d+)?)*")

HYBRID = "hybrid: the fastest cores for the clients, the others for the mock"
OVERRIDE = "override"
ONE_TYPE = "even: one core type"
UNKNOWN = "even: core types unknown"


class CoreError(Exception):
    """A core list or a split that cannot be used."""


class _Parser(argparse.ArgumentParser):
    def error(self, message):
        raise CoreError(message)


def parse_list(text, name, count):
    """Expands a list such as 0-3,8 into sorted, distinct core indexes below count."""
    text = text.strip()
    if not LIST.fullmatch(text):
        raise CoreError(f"{name} must be a core list such as 0-3,8, not '{text}'.")
    cores = set()
    for part in text.split(","):
        first, _, last = part.partition("-")
        first, last = int(first), int(last or first)
        if last < first:
            raise CoreError(f"{name} has a descending range: {part}.")
        cores.update(range(first, last + 1))
    highest = max(cores)
    if highest >= count:
        raise CoreError(f"{name} names core {highest}, but this machine has cores 0 to {count - 1}.")
    return sorted(cores)


def describe(cores):
    """Writes sorted core indexes as a canonical list such as 0-3,8, the form the .NET harness writes."""
    parts = []
    i = 0
    while i < len(cores):
        j = i
        while j + 1 < len(cores) and cores[j + 1] == cores[j] + 1:
            j += 1
        parts.append(f"{cores[i]}-{cores[j]}" if j > i else f"{cores[i]}")
        i = j + 1
    return ",".join(parts)


def mask(cores):
    """The affinity mask with bit n set for core n."""
    value = 0
    for core in cores:
        value |= 1 << core
    return value


def signed64(value):
    """The same 64 bits as a signed number: core 63's bit becomes the sign bit, as .NET's nint expects."""
    return value - (1 << 64) if value >= 1 << 63 else value


def parse_siblings(text, count):
    """Reads --siblings: groups of logical cores, one per physical core, separated by ';', covering every core once."""
    groups = [parse_list(part, "--siblings", count) for part in text.split(";")]
    return _check_siblings(groups, count, "--siblings")


def _check_siblings(groups, count, name):
    seen = sorted(core for group in groups for core in group)
    if seen != list(range(count)):
        raise CoreError(f"{name} must name each of cores 0 to {count - 1} in exactly one group.")
    return sorted(groups, key=lambda group: group[0])


def detect_siblings(sysfs, count):
    """Each physical core's logical cores from sysfs, or None when sysfs does not say for every core."""
    groups = {}
    for core in range(count):
        text = _read(os.path.join(sysfs, "devices", "system", "cpu", f"cpu{core}", "topology", "thread_siblings_list"))
        if text is None:
            return None
        try:
            # A sibling above the cores the split uses, on a machine with more than 64, is left out.
            siblings = tuple(c for c in parse_list(text, "thread_siblings_list", 1 << 16) if c < count)
        except CoreError:
            return None
        if core not in siblings:
            return None
        groups[siblings] = list(siblings)
    try:
        return _check_siblings(list(groups.values()), count, "thread_siblings_list")
    except CoreError:
        return None


def describe_siblings(groups):
    """Writes the physical cores as their logical-core lists separated by ';', or 'unknown'."""
    return ";".join(describe(group) for group in groups) if groups is not None else "unknown"


def parse_ranks(text, count):
    """Reads --ranks: one integer per logical core, in index order."""
    parts = text.split(",")
    if len(parts) != count or not all(p.strip().isdigit() for p in parts):
        raise CoreError(f"--ranks must list one number per core, {count} in all.")
    return {core: int(p) for core, p in enumerate(parts)}


def _read(path):
    try:
        with open(path, encoding="ascii") as f:
            return f.read().strip()
    except OSError:
        return None


def detect_ranks(sysfs, count):
    """Each core's type from sysfs, higher being faster, or None when sysfs does not say."""
    big = _read(os.path.join(sysfs, "devices", "cpu_core", "cpus"))
    little = _read(os.path.join(sysfs, "devices", "cpu_atom", "cpus"))
    if big and little:
        ranks = {core: 1 for core in parse_list(big, "cpu_core", count)}
        ranks.update({core: 0 for core in parse_list(little, "cpu_atom", count)})
        return ranks if len(ranks) == count else None

    ranks = {}
    for core in range(count):
        capacity = _read(os.path.join(sysfs, "devices", "system", "cpu", f"cpu{core}", "cpu_capacity"))
        if capacity is None or not capacity.isdigit():
            return None
        ranks[core] = int(capacity)
    return ranks


def split(count, mock_cores=None, client_cores=None, ranks=None, siblings=None):
    """Returns (mock cores, client cores, how the split was made).

    siblings lists each physical core's logical cores; without it every logical core is its own physical core.
    """
    count = min(count, MAX_CORES)
    if count < 2:
        raise CoreError("The mock and the clients need separate cores, and this machine has one.")
    every = list(range(count))
    groups = siblings if siblings is not None else [[core] for core in every]
    if mock_cores is not None or client_cores is not None:
        mock = parse_list(mock_cores, "--mock-cores", count) if mock_cores is not None else None
        clients = parse_list(client_cores, "--client-cores", count) if client_cores is not None else None
        if mock is None:
            mock = [c for c in every if c not in clients]
        if clients is None:
            clients = [c for c in every if c not in mock]
        how = OVERRIDE
    elif ranks is not None and len(set(ranks.values())) > 1:
        # A physical core's threads share its type; the highest of them counts, should a source disagree.
        fastest = max(ranks.values())
        fast = [g for g in groups if max(ranks[c] for c in g) == fastest]
        clients = sorted(c for g in fast for c in g)
        mock = sorted(c for g in groups if g not in fast for c in g)
        how = HYBRID
    else:
        if len(groups) < 2:
            raise CoreError(
                "The machine has one physical core, so the mock and the clients would share it; pass --mock-cores and"
                " --client-cores to split its threads anyway."
            )
        half = len(groups) // 2
        mock = sorted(c for g in groups[:half] for c in g)
        clients = sorted(c for g in groups[half:] for c in g)
        how = UNKNOWN if ranks is None else ONE_TYPE

    if not mock or not clients:
        raise CoreError("The mock and the clients each need at least one core; pass --mock-cores and --client-cores.")
    if set(mock) & set(clients):
        raise CoreError("--mock-cores and --client-cores overlap.")
    return mock, clients, how


def shared_cores(mock, clients, siblings):
    """The physical cores, as logical-core lists, that have threads on both sides."""
    if siblings is None:
        return []
    return [g for g in siblings if set(g) & set(mock) and set(g) & set(clients)]


def main(argv):
    parser = _Parser(add_help=False, allow_abbrev=False)
    parser.add_argument("--count", type=int, default=os.cpu_count() or 1)
    parser.add_argument("--mock-cores")
    parser.add_argument("--client-cores")
    parser.add_argument("--ranks")
    parser.add_argument("--siblings")
    parser.add_argument("--sysfs", default="/sys")
    try:
        opts = parser.parse_args(argv)
        count = min(opts.count, MAX_CORES)
        ranks = parse_ranks(opts.ranks, count) if opts.ranks else detect_ranks(opts.sysfs, count)
        siblings = parse_siblings(opts.siblings, count) if opts.siblings else detect_siblings(opts.sysfs, count)
        mock, clients, how = split(count, opts.mock_cores, opts.client_cores, ranks, siblings)
    except CoreError as error:
        print(str(error), file=sys.stderr)
        return 2
    for group in shared_cores(mock, clients, siblings):
        print(f"warning: the override splits physical core {describe(group)} between the mock and the clients.", file=sys.stderr)
    client_mask = mask(clients)
    sys.stdout.reconfigure(newline="\n")
    print(f"mock={describe(mock)}")
    print(f"clients={describe(clients)}")
    print(f"clientmask={client_mask}")
    print(f"clientmask64={signed64(client_mask)}")
    print(f"split={how}")
    print(f"siblings={describe_siblings(siblings)}")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
