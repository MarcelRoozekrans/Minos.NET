// Benchmarks TypeSafe's official JS SDK against the shared local mock, the way the .NET harness measures its clients:
// a checked start-up call, a warmed-up sequential latency loop, then a 16-worker throughput run, each counted against
// the mock's GET /count.
import { mkdir, writeFile } from "node:fs/promises";
import os from "node:os";
import path from "node:path";
import { createRequire } from "node:module";
import { TypeSafeClient, choice, noul } from "@typesafe-ai/sdk";

const USAGE =
  "Usage: node bench.mjs --base-url <url> --out <dir> [--smoke] [--machine <name>] [--cores <list>] [--mock-cores <list>]";
// A core list such as 0-11 or 0,2,4-6. The runner pins Node from outside, so the harness only records these.
const CORE_LIST = /^\d+(-\d+)?(,\d+(-\d+)?)*$/;
const CONCURRENCY = 16;
const DUMMY_API_KEY = "benchmark-dummy-key";
const STATE = "Is my booking to Rome still on? I fly tonight.";
const MODEL = "jev-latest";
const EXPECTED = {
  model: "typesafe/jev-1.13-20260917",
  choice: "look_up_booking",
  confidence: 0.99,
  probabilities: { look_up_booking: 0.99, change_booking: 0, other: 0.01, dispute_charge: 0 },
  noul: 0.86,
};

// .NET's Environment.MachineName, the default the .NET harness uses: the NetBIOS name on Windows, upper case and cut to
// 15 characters; the host name as-is elsewhere.
const defaultMachine = () => (process.platform === "win32" ? os.hostname().toUpperCase().slice(0, 15) : os.hostname());

// Reads like .NET's RuntimeInformation.OSDescription: "Microsoft Windows 10.0.26200" on Windows, and uname -srv on
// Linux and macOS, for example "Linux 6.8.0-45-generic #45-Ubuntu SMP". os.version() is the Windows edition name there,
// not the build, so Windows uses os.release() alone.
const osDescription = () =>
  process.platform === "win32" ? `Microsoft Windows ${os.release()}` : `${os.type()} ${os.release()} ${os.version()}`;

class CheckError extends Error {}
class UsageError extends Error {}

function parseArgs(argv) {
  const opts = { baseUrl: null, out: null, smoke: false, machine: defaultMachine(), cores: null, mockCores: null };
  for (let i = 0; i < argv.length; i++) {
    const arg = argv[i];
    if (arg === "--smoke") {
      opts.smoke = true;
    } else if (arg === "--base-url" || arg === "--out" || arg === "--machine") {
      const value = argv[++i];
      if (value === undefined) throw new UsageError(`${arg} needs a value.`);
      if (arg === "--base-url") opts.baseUrl = value;
      else if (arg === "--out") opts.out = value;
      else opts.machine = value;
    } else if (arg === "--cores" || arg === "--mock-cores") {
      const value = argv[++i];
      if (value === undefined || !CORE_LIST.test(value)) {
        throw new UsageError(`${arg} must be a core list such as 0-3,8.`);
      }
      if (arg === "--cores") opts.cores = value;
      else opts.mockCores = value;
    } else {
      throw new UsageError(`Unknown argument: ${arg}`);
    }
  }
  let url;
  try {
    url = new URL(opts.baseUrl ?? "");
  } catch {
    throw new UsageError("--base-url must be an absolute http or https address.");
  }
  if (url.protocol !== "http:" && url.protocol !== "https:") {
    throw new UsageError("--base-url must be an absolute http or https address.");
  }
  if (!opts.out || opts.out.trim() === "") throw new UsageError("--out is required.");
  if (opts.machine.trim() === "") throw new UsageError("--machine must not be blank.");
  opts.baseUrl = url.href.replace(/\/+$/, "");
  opts.machine = opts.machine.trim();
  return opts;
}

const questions = {
  intent: choice("What does the traveller want?", {
    look_up_booking: "Wants to see or look up an existing booking",
    change_booking: "Wants to change dates, names, seats or luggage on a booking",
    dispute_charge: "Disputes a charge or asks for money back",
    other: "Anything else",
  }),
  travels_within24_hours: noul("Does the request mention travelling within the next 24 hours?"),
};

function sameProbabilities(actual) {
  const keys = Object.keys(EXPECTED.probabilities);
  return (
    actual != null &&
    Object.keys(actual).length === keys.length &&
    keys.every((k) => actual[k] === EXPECTED.probabilities[k])
  );
}

function isExpected(result) {
  return (
    result.answers.intent.choice === EXPECTED.choice &&
    result.answers.travels_within24_hours.noul === EXPECTED.noul
  );
}

function assertFullAnswers(result) {
  const intent = result.answers.intent;
  const noulAnswer = result.answers.travels_within24_hours.noul;
  const problems = [];
  if (result.model !== EXPECTED.model) problems.push(`model ${result.model}`);
  if (intent.choice !== EXPECTED.choice) problems.push(`intent.choice ${intent.choice}`);
  if (intent.confidence !== EXPECTED.confidence) problems.push(`intent.confidence ${intent.confidence}`);
  if (!sameProbabilities(intent.probabilities)) {
    problems.push(`intent.probabilities ${JSON.stringify(intent.probabilities)}`);
  }
  if (noulAnswer !== EXPECTED.noul) problems.push(`travels_within24_hours.noul ${noulAnswer}`);
  if (problems.length > 0) {
    throw new CheckError(`The start-up call read unexpected answers: ${problems.join("; ")}.`);
  }
}

async function readCount(baseUrl) {
  const response = await fetch(`${baseUrl}/count`);
  const text = (await response.text()).trim();
  if (!response.ok || !/^\d+$/.test(text)) {
    throw new CheckError(`GET /count answered ${response.status} ${text}`);
  }
  return Number(text);
}

// Reads the mock's count before and after the calls, and fails unless it rose by exactly the calls made.
async function counted(baseUrl, run, makeCalls) {
  const before = await readCount(baseUrl);
  const result = await makeCalls();
  const after = await readCount(baseUrl);
  if (after - before !== result.calls) {
    throw new CheckError(
      `The ${run} made ${result.calls} calls but the mock served ${after - before} requests. ` +
        "A retry or an extra request breaks the one-request-per-call rule.",
    );
  }
  return result;
}

const sdkCall = (client) => client.systemOne({ state: STATE, questions, model: MODEL });

// The smallest value with at least the given percentage of values at or below it.
function nearestRank(sorted, percentile) {
  const rank = Math.ceil((percentile / 100) * sorted.length);
  return sorted[Math.max(rank, 1) - 1];
}

const round3 = (value) => Math.round(value * 1000) / 1000;

function summarize(milliseconds) {
  const sorted = [...milliseconds].sort((a, b) => a - b);
  const mean = sorted.reduce((sum, v) => sum + v, 0) / sorted.length;
  return { mean: round3(mean), p50: round3(nearestRank(sorted, 50)), p99: round3(nearestRank(sorted, 99)) };
}

async function latencyRun(client, warmupCalls, timedCalls) {
  for (let i = 0; i < warmupCalls; i++) {
    if (!isExpected(await sdkCall(client))) {
      throw new CheckError("The SDK read an unexpected answer in the latency loop.");
    }
  }
  const milliseconds = [];
  for (let i = 0; i < timedCalls; i++) {
    const start = process.hrtime.bigint();
    const result = await sdkCall(client);
    milliseconds.push(Number(process.hrtime.bigint() - start) / 1e6);
    if (!isExpected(result)) throw new CheckError("The SDK read an unexpected answer in the latency loop.");
  }
  return { calls: warmupCalls + timedCalls, latency: summarize(milliseconds) };
}

// Each worker starts a new call until the time is up; a call in flight then completes and counts.
async function throughputPhase(client, workers, durationMs) {
  let calls = 0;
  const start = process.hrtime.bigint();
  const elapsedMs = () => Number(process.hrtime.bigint() - start) / 1e6;
  const work = async () => {
    while (elapsedMs() < durationMs) {
      const result = await sdkCall(client);
      calls++;
      if (!isExpected(result)) {
        throw new CheckError("The SDK read an unexpected answer during the throughput run.");
      }
    }
  };
  await Promise.all(Array.from({ length: workers }, work));
  return { calls, perSecond: calls / (elapsedMs() / 1000) };
}

async function main() {
  const opts = parseArgs(process.argv.slice(2));
  const latencyWarmup = opts.smoke ? 10 : 200;
  const latencyCalls = opts.smoke ? 20 : 2000;
  const throughputWarmupMs = opts.smoke ? 500 : 2000;
  const throughputMs = opts.smoke ? 1000 : 10000;

  // One long-lived client, one attempt per call.
  const client = new TypeSafeClient({ apiKey: DUMMY_API_KEY, baseURL: opts.baseUrl, retry: { maxRetries: 0 } });

  // Start-up check: one counted call whose answers must equal the workload's expected values.
  await counted(opts.baseUrl, "start-up call", async () => {
    assertFullAnswers(await sdkCall(client));
    return { calls: 1 };
  });

  // The same warm-up the .NET harness gives every client, on the instance it measures, before its latency loop: 16
  // workers for 2 s, or 0.5 s in a smoke run, so the latency loop runs on optimized code and warm connections, as the
  // .NET clients' does.
  const processWarmup = await counted(opts.baseUrl, "process warm-up", () =>
    throughputPhase(client, CONCURRENCY, throughputWarmupMs),
  );
  console.log(`typesafe-ai-sdk-js: process warm-up ${processWarmup.calls} of ${processWarmup.calls} requests`);

  const latency = await counted(opts.baseUrl, "latency loop", () =>
    latencyRun(client, latencyWarmup, latencyCalls),
  );
  const warmup = await counted(opts.baseUrl, "throughput warm-up", () =>
    throughputPhase(client, CONCURRENCY, throughputWarmupMs),
  );
  const throughput = await counted(opts.baseUrl, "throughput run", () =>
    throughputPhase(client, CONCURRENCY, throughputMs),
  );

  console.log(
    `typesafe-ai-sdk-js: latency ${latency.calls} of ${latency.calls} requests, mean ${latency.latency.mean} ms, ` +
      `p50 ${latency.latency.p50} ms, p99 ${latency.latency.p99} ms; ` +
      `throughput warm-up ${warmup.calls} of ${warmup.calls}, ` +
      `measured ${throughput.calls} of ${throughput.calls}, ${throughput.perSecond.toFixed(0)}/s`,
  );

  const sdkVersion = createRequire(import.meta.url)("@typesafe-ai/sdk/package.json").version;
  const file = {
    machine: {
      name: opts.machine,
      os: osDescription(),
      cpu: os.cpus()[0]?.model ?? process.arch,
      date: new Date().toISOString().replace(/\.\d+Z$/, "Z"),
      mockCeilingPerSecond: null,
      cores: opts.cores,
      mockCores: opts.mockCores,
    },
    results: [
      {
        client: "typesafe-ai-sdk-js",
        library: "@typesafe-ai/sdk",
        version: sdkVersion,
        runtime: "Node.js",
        runtimeVersion: process.versions.node,
        latencyMs: latency.latency,
        throughputPerSecond: Math.round(throughput.perSecond * 10) / 10,
        concurrency: CONCURRENCY,
        allocatedBytesPerCall: null,
      },
    ],
  };
  const safe = opts.machine.replace(/[^A-Za-z0-9._-]/g, "-");
  await mkdir(opts.out, { recursive: true });
  const target = path.join(opts.out, `js-${safe}.json`);
  await writeFile(target, JSON.stringify(file, null, 2) + "\n");
  console.log(`Wrote ${target}`);
}

try {
  await main();
} catch (error) {
  console.error(error instanceof Error ? error.message : String(error));
  if (error instanceof UsageError) {
    console.error(USAGE);
    process.exit(2);
  }
  process.exit(1);
}
