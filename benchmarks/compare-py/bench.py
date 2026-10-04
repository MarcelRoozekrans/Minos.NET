"""Benchmarks TypeSafe's official Python SDK against the shared local mock, the way the .NET and JS harnesses measure
their clients: a checked start-up call, a warmed-up sequential latency loop, then a 16-worker throughput run, each
counted against the mock's GET /count."""

import argparse
import asyncio
import json
import math
import os
import platform
import re
import sys
import time
import urllib.parse
import urllib.request
from datetime import datetime, timezone
from importlib.metadata import version

from typesafe_sdk import AsyncTypeSafeClient, Choice, Noul, RetryPolicy, TypeSafeClient

USAGE = "Usage: python bench.py --base-url <url> --out <dir> [--smoke] [--machine <name>]"
CONCURRENCY = 16
DUMMY_API_KEY = "benchmark-dummy-key"
STATE = "Is my booking to Rome still on? I fly tonight."
MODEL = "jev-latest"
EXPECTED_MODEL = "typesafe/jev-1.13-20260917"
EXPECTED_CHOICE = "look_up_booking"
EXPECTED_CONFIDENCE = 0.99
EXPECTED_PROBABILITIES = {"look_up_booking": 0.99, "change_booking": 0, "other": 0.01, "dispute_charge": 0}
EXPECTED_NOUL = 0.86

QUESTIONS = {
    "intent": Choice(
        instructions="What does the traveller want?",
        criteria={
            "look_up_booking": "Wants to see or look up an existing booking",
            "change_booking": "Wants to change dates, names, seats or luggage on a booking",
            "dispute_charge": "Disputes a charge or asks for money back",
            "other": "Anything else",
        },
    ),
    "travels_within24_hours": Noul(
        instructions="Does the request mention travelling within the next 24 hours?",
    ),
}


class CheckError(Exception):
    pass


class UsageError(Exception):
    pass


def default_machine():
    """.NET's Environment.MachineName: the NetBIOS name on Windows, upper case and cut to 15 characters; the host name
    as-is elsewhere."""
    name = platform.node()
    return name.upper()[:15] if sys.platform == "win32" else name


def os_description():
    """Reads like .NET's RuntimeInformation.OSDescription: "Microsoft Windows 10.0.26200" on Windows, and uname -srv on
    Linux and macOS."""
    if sys.platform == "win32":
        return f"Microsoft Windows {platform.version()}"
    u = platform.uname()
    return f"{u.system} {u.release} {u.version}"


def cpu_name():
    if sys.platform == "win32":
        try:
            import winreg

            with winreg.OpenKey(winreg.HKEY_LOCAL_MACHINE, r"HARDWARE\DESCRIPTION\System\CentralProcessor\0") as key:
                return winreg.QueryValueEx(key, "ProcessorNameString")[0].strip()
        except OSError:
            pass
    elif sys.platform.startswith("linux"):
        try:
            with open("/proc/cpuinfo", encoding="utf-8") as f:
                for line in f:
                    if line.lower().startswith("model name"):
                        return line.split(":", 1)[1].strip()
        except OSError:
            pass
    return platform.processor() or platform.machine()


_NO_PROXY = urllib.request.build_opener(urllib.request.ProxyHandler({}))


def _number(value: float) -> float | int:
    """Writes a whole number without a trailing .0, as the JS and .NET harnesses do."""
    return int(value) if value.is_integer() else value


class _Parser(argparse.ArgumentParser):
    def error(self, message):
        raise UsageError(message)


def parse_args(argv):
    parser = _Parser(add_help=False, allow_abbrev=False)
    parser.add_argument("--base-url")
    parser.add_argument("--out")
    parser.add_argument("--smoke", action="store_true")
    parser.add_argument("--machine", default=None)
    opts = parser.parse_args(argv)
    url = urllib.parse.urlsplit(opts.base_url or "")
    if url.scheme not in ("http", "https") or not url.netloc:
        raise UsageError("--base-url must be an absolute http or https address.")
    if not opts.out or not opts.out.strip():
        raise UsageError("--out is required.")
    machine = default_machine() if opts.machine is None else opts.machine
    if not machine.strip():
        raise UsageError("--machine must not be blank.")
    opts.base_url = opts.base_url.rstrip("/")
    opts.machine = machine.strip()
    return opts


def is_expected(result):
    return (
        result.answers["intent"].choice == EXPECTED_CHOICE
        and result.answers["travels_within24_hours"].noul == EXPECTED_NOUL
    )


def assert_full_answers(result):
    intent = result.answers["intent"]
    noul = result.answers["travels_within24_hours"].noul
    problems = []
    if result.model != EXPECTED_MODEL:
        problems.append(f"model {result.model}")
    if intent.choice != EXPECTED_CHOICE:
        problems.append(f"intent.choice {intent.choice}")
    if intent.confidence != EXPECTED_CONFIDENCE:
        problems.append(f"intent.confidence {intent.confidence}")
    if dict(intent.probabilities) != EXPECTED_PROBABILITIES:
        problems.append(f"intent.probabilities {dict(intent.probabilities)}")
    if noul != EXPECTED_NOUL:
        problems.append(f"travels_within24_hours.noul {noul}")
    if problems:
        raise CheckError(f"The start-up call read unexpected answers: {'; '.join(problems)}.")


def read_count(base_url):
    # Read the count directly, never through a proxy from HTTP_PROXY, so it is the mock's own figure.
    with _NO_PROXY.open(f"{base_url}/count") as response:
        status = response.status
        text = response.read().decode("utf-8").strip()
    if status != 200 or not re.fullmatch(r"\d+", text):
        raise CheckError(f"GET /count answered {status} {text}")
    return int(text)


def check_count(run, calls, before, after):
    if after - before != calls:
        raise CheckError(
            f"The {run} made {calls} calls but the mock served {after - before} requests. "
            "A retry or an extra request breaks the one-request-per-call rule."
        )


def counted(base_url, run, make_calls):
    """Reads the mock's count before and after the calls, and fails unless it rose by exactly the calls made."""
    before = read_count(base_url)
    result = make_calls()
    check_count(run, result["calls"], before, read_count(base_url))
    return result


def nearest_rank(sorted_values, percentile):
    """The smallest value with at least the given percentage of values at or below it."""
    rank = math.ceil((percentile / 100) * len(sorted_values))
    return sorted_values[max(rank, 1) - 1]


def round3(value):
    return math.floor(value * 1000 + 0.5) / 1000


def summarize(milliseconds):
    values = sorted(milliseconds)
    mean = sum(values) / len(values)
    return {"mean": round3(mean), "p50": round3(nearest_rank(values, 50)), "p99": round3(nearest_rank(values, 99))}


def latency_run(client, warmup_calls, timed_calls):
    for _ in range(warmup_calls):
        if not is_expected(client.system_one(STATE, QUESTIONS, model=MODEL)):
            raise CheckError("The SDK read an unexpected answer in the latency loop.")
    milliseconds = []
    for _ in range(timed_calls):
        start = time.perf_counter_ns()
        result = client.system_one(STATE, QUESTIONS, model=MODEL)
        milliseconds.append((time.perf_counter_ns() - start) / 1e6)
        if not is_expected(result):
            raise CheckError("The SDK read an unexpected answer in the latency loop.")
    return {"calls": warmup_calls + timed_calls, "latency": summarize(milliseconds)}


async def throughput_phase(client, workers, duration_ms):
    """Each worker starts a new call until the time is up; a call in flight then completes and counts."""
    calls = 0
    start = time.perf_counter_ns()

    def elapsed_ms():
        return (time.perf_counter_ns() - start) / 1e6

    async def work():
        nonlocal calls
        while elapsed_ms() < duration_ms:
            result = await client.system_one(STATE, QUESTIONS, model=MODEL)
            calls += 1
            if not is_expected(result):
                raise CheckError("The SDK read an unexpected answer during the throughput run.")

    await asyncio.gather(*(work() for _ in range(workers)))
    return {"calls": calls, "per_second": calls / (elapsed_ms() / 1000)}


async def throughput_runs(base_url, warmup_ms, measured_ms):
    # One long-lived async client, one attempt per call, closed when both windows end. The count reads are blocking, but
    # no call is in flight when they run.
    async with AsyncTypeSafeClient(
        api_key=DUMMY_API_KEY, base_url=base_url, retry=RetryPolicy(max_retries=0)
    ) as client:
        results = []
        for run, duration_ms in (("throughput warm-up", warmup_ms), ("throughput run", measured_ms)):
            before = read_count(base_url)
            result = await throughput_phase(client, CONCURRENCY, duration_ms)
            check_count(run, result["calls"], before, read_count(base_url))
            results.append(result)
    return results


def main():
    opts = parse_args(sys.argv[1:])
    latency_warmup = 10 if opts.smoke else 200
    latency_calls = 20 if opts.smoke else 2000
    warmup_ms = 500 if opts.smoke else 2000
    measured_ms = 1000 if opts.smoke else 10000

    # One long-lived sync client, one attempt per call, closed when the latency loop ends.
    with TypeSafeClient(api_key=DUMMY_API_KEY, base_url=opts.base_url, retry=RetryPolicy(max_retries=0)) as client:

        def startup():
            # Start-up check: one counted call whose answers must equal the workload's expected values.
            assert_full_answers(client.system_one(STATE, QUESTIONS, model=MODEL))
            return {"calls": 1}

        counted(opts.base_url, "start-up call", startup)
        latency = counted(opts.base_url, "latency loop", lambda: latency_run(client, latency_warmup, latency_calls))

    warmup, throughput = asyncio.run(throughput_runs(opts.base_url, warmup_ms, measured_ms))

    print(
        f"typesafe-sdk-python: latency {latency['calls']} of {latency['calls']} requests, "
        f"mean {latency['latency']['mean']} ms, p50 {latency['latency']['p50']} ms, p99 {latency['latency']['p99']} ms; "
        f"throughput warm-up {warmup['calls']} of {warmup['calls']}, "
        f"measured {throughput['calls']} of {throughput['calls']}, {throughput['per_second']:.0f}/s"
    )

    result_file = {
        "machine": {
            "name": opts.machine,
            "os": os_description(),
            "cpu": cpu_name(),
            "date": datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"),
            "mockCeilingPerSecond": None,
            "cores": None,
        },
        "results": [
            {
                "client": "typesafe-sdk-python",
                "library": "typesafe-sdk",
                "version": version("typesafe-sdk"),
                "runtime": "Python",
                "runtimeVersion": platform.python_version(),
                "latencyMs": latency["latency"],
                "throughputPerSecond": _number(math.floor(throughput["per_second"] * 10 + 0.5) / 10),
                "concurrency": CONCURRENCY,
                "allocatedBytesPerCall": None,
            }
        ],
    }
    safe = re.sub(r"[^A-Za-z0-9._-]", "-", opts.machine)
    os.makedirs(opts.out, exist_ok=True)
    target = os.path.join(opts.out, f"py-{safe}.json")
    with open(target, "w", encoding="utf-8", newline="\n") as f:
        f.write(json.dumps(result_file, indent=2) + "\n")
    print(f"Wrote {target}")


if __name__ == "__main__":
    try:
        main()
    except Exception as error:  # any failure is reported and exits non-zero
        print(str(error) or type(error).__name__, file=sys.stderr)
        if isinstance(error, UsageError):
            print(USAGE, file=sys.stderr)
            sys.exit(2)
        sys.exit(1)
