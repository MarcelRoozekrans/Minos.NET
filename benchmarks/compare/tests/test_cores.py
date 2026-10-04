"""Tests for cores.py, the core split both runners use."""

import os
import subprocess
import sys

import pytest

sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))

import cores  # noqa: E402

CORES = os.path.join(os.path.dirname(__file__), "..", "cores.py")


def write(root, relative, text):
    path = root / relative
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(text, encoding="ascii")


@pytest.mark.parametrize(
    ("text", "expected"),
    [("0", [0]), ("0-3,8", [0, 1, 2, 3, 8]), ("8,0-3,2", [0, 1, 2, 3, 8]), (" 12-19 ", list(range(12, 20)))],
)
def test_lists_expand_to_sorted_distinct_cores(text, expected):
    assert cores.parse_list(text, "--x", 20) == expected


@pytest.mark.parametrize(("text", "reason"), [("", "core list"), ("1-", "core list"), ("a", "core list"), ("3-1", "descending"), ("20", "core 20")])
def test_bad_lists_are_refused(text, reason):
    with pytest.raises(cores.CoreError, match=reason):
        cores.parse_list(text, "--x", 20)


@pytest.mark.parametrize(("listed", "text"), [([0], "0"), ([0, 1, 2, 3, 8], "0-3,8"), ([1, 3, 5], "1,3,5"), (list(range(12, 20)), "12-19")])
def test_cores_are_described_canonically(listed, text):
    assert cores.describe(listed) == text


def test_core_63_fits_a_signed_64_bit_mask():
    value = cores.mask([0, 63])

    assert value == 0x8000000000000001
    assert cores.signed64(value) == -0x7FFFFFFFFFFFFFFF
    assert cores.signed64(cores.mask(range(64))) == -1
    assert cores.signed64(cores.mask([62])) == 1 << 62


def test_a_hybrid_cpu_gives_the_clients_the_fastest_cores():
    ranks = {c: (1 if c < 12 else 0) for c in range(20)}

    assert cores.split(20, ranks=ranks) == (list(range(12, 20)), list(range(12)), cores.HYBRID)


def test_one_core_type_splits_evenly():
    assert cores.split(20, ranks={c: 0 for c in range(20)}) == (list(range(10)), list(range(10, 20)), cores.ONE_TYPE)


def test_unknown_core_types_split_evenly_and_say_so():
    assert cores.split(5) == ([0, 1], [2, 3, 4], cores.UNKNOWN)


def test_one_override_gives_the_other_side_the_rest():
    assert cores.split(8, mock_cores="6-7") == ([6, 7], list(range(6)), cores.OVERRIDE)
    assert cores.split(8, client_cores="0-3") == ([4, 5, 6, 7], [0, 1, 2, 3], cores.OVERRIDE)


@pytest.mark.parametrize(
    ("kwargs", "reason"),
    [
        ({"mock_cores": "0-3", "client_cores": "3-5"}, "overlap"),
        ({"mock_cores": "0-7"}, "at least one core"),
        ({"client_cores": "99"}, "core 99"),
    ],
)
def test_impossible_splits_are_refused(kwargs, reason):
    with pytest.raises(cores.CoreError, match=reason):
        cores.split(8, **kwargs)


def test_a_one_core_machine_is_refused():
    with pytest.raises(cores.CoreError, match="one"):
        cores.split(1)


def test_sysfs_intel_hybrid_folders(tmp_path):
    write(tmp_path, "devices/cpu_core/cpus", "0-11\n")
    write(tmp_path, "devices/cpu_atom/cpus", "12-19\n")

    ranks = cores.detect_ranks(str(tmp_path), 20)

    assert cores.split(20, ranks=ranks)[:2] == (list(range(12, 20)), list(range(12)))


def test_sysfs_cpu_capacity_counts_differing_capacities_as_types(tmp_path):
    for core in range(8):
        write(tmp_path, f"devices/system/cpu/cpu{core}/cpu_capacity", "1024\n" if core >= 4 else "446\n")

    ranks = cores.detect_ranks(str(tmp_path), 8)

    assert cores.split(8, ranks=ranks) == ([0, 1, 2, 3], [4, 5, 6, 7], cores.HYBRID)


def test_sysfs_equal_capacities_are_one_type(tmp_path):
    for core in range(4):
        write(tmp_path, f"devices/system/cpu/cpu{core}/cpu_capacity", "1024\n")

    assert cores.split(4, ranks=cores.detect_ranks(str(tmp_path), 4))[2] == cores.ONE_TYPE


def test_sysfs_without_either_source_is_unknown(tmp_path):
    write(tmp_path, "devices/system/cpu/cpu0/cpu_capacity", "1024\n")

    assert cores.detect_ranks(str(tmp_path), 4) is None


def run(*args):
    return subprocess.run([sys.executable, CORES, *args], capture_output=True, text=True, check=False)


def test_the_command_line_prints_key_value_lines(tmp_path):
    result = run("--count", "20", "--ranks", ",".join(["1"] * 12 + ["0"] * 8), "--sysfs", str(tmp_path))

    assert result.returncode == 0, result.stderr
    assert result.stdout.splitlines() == [
        "mock=12-19",
        "clients=0-11",
        "clientmask=4095",
        "clientmask64=4095",
        f"split={cores.HYBRID}",
    ]


def test_the_command_line_reads_sysfs_without_ranks(tmp_path):
    result = run("--count", "4", "--sysfs", str(tmp_path))

    assert result.returncode == 0, result.stderr
    assert f"split={cores.UNKNOWN}" in result.stdout.splitlines()


@pytest.mark.parametrize(
    "args",
    [
        ["--count", "8", "--mock-cores", "0-3", "--client-cores", "3"],
        ["--count", "4", "--ranks", "1,0"],
        ["--count", "8", "--bogus"],
    ],
)
def test_a_wrong_command_line_exits_with_2(args):
    result = run(*args)

    assert result.returncode == 2
    assert result.stderr.strip()
    assert result.stdout == ""
