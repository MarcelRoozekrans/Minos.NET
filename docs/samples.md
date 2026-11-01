---
id: samples
title: Samples
sidebar_position: 18
description: Six runnable cookbook samples: five run offline, four replaying recorded answers, one of them against three providers, and one over a chat client, and the sixth replays a dataset through shadow mode. How to run, record and trust them.
---

# Samples

The repository holds six small console apps that show Minos doing real work. They are cookbook samples: each one is a
whole program you can run, read and change, with its own README. They are our own work. Three are inspired by a
TypeSafe cookbook or pattern, which their READMEs credit, and the code, the data and the policies are written here.

## The six samples

| Sample | What it shows | Run it | Live requests |
| --- | --- | --- | --- |
| [Guardrails](https://github.com/MarcelRoozekrans/Minos.NET/tree/main/samples/Minos.NET.Samples.Guardrails) | Screens 15 chat messages before they reach a language model: one request per message, and two policies over the same answers. | `dotnet run --project samples/Minos.NET.Samples.Guardrails` | 15 |
| [Chat client](https://github.com/MarcelRoozekrans/Minos.NET/tree/main/samples/Minos.NET.Samples.ChatClient) | Asks a chat model two questions about each of 3 support tickets through `Minos.NET.Extensions.AI`, records them, and prints where each answer's probabilities came from. | `dotnet run --project samples/Minos.NET.Samples.ChatClient` | 6, and one more per question without logprobs, only with a key |
| [Intent routing](https://github.com/MarcelRoozekrans/Minos.NET/tree/main/samples/Minos.NET.Samples.IntentRouting) | Decides for each of 12 travel requests whether code, an assistant model or a person handles it, with Jev as the cheap first step. | `dotnet run --project samples/Minos.NET.Samples.IntentRouting` | 12 |
| [Provider switch](https://github.com/MarcelRoozekrans/Minos.NET/tree/main/samples/Minos.NET.Samples.ProviderSwitch) | Routes 8 support tickets to a team with one Choice question, through TypeSafe, OpenRouter or a local server, chosen by configuration only. | `dotnet run --project samples/Minos.NET.Samples.ProviderSwitch` | 8 per provider |
| [Re-ranking](https://github.com/MarcelRoozekrans/Minos.NET/tree/main/samples/Minos.NET.Samples.Reranking) | Re-orders a keyword shortlist of help articles by how well each answers the question, in one fan-out request per question. | `dotnet run --project samples/Minos.NET.Samples.Reranking` | 5 |
| [Shadow replay](https://github.com/MarcelRoozekrans/Minos.NET/tree/main/samples/Minos.NET.Samples.ShadowReplay) | Replays a labelled Banking77 dataset through the recorder's shadow mode: a hosted primary decides each item, a local candidate shadows it, and both sets of answers go to a records file. It has no recordings and no offline mode. | `dotnet run --project samples/Minos.NET.Samples.ShadowReplay` | 1 billed per dataset item |

The samples pair with the guide: Guardrails reads several answers from one request, as
[fan-out](patterns/fan-out.md) does. Intent routing is the [intent routing](patterns/intent-routing.md) pattern with a
confidence gate. Provider switch is [switching providers](providers.md#switching-providers) as a whole program.
Re-ranking builds its questions at run time, as [question sets at run time](question-sets-at-run-time.md) describes.
Chat client is [chat clients](chat-clients.md) as a whole program.
The guide's pages are short and self-contained, and the samples are whole programs: configuration, a host, a report.

Each link goes to the sample's folder on GitHub, where its README explains its design, its numbers and what to
watch for.

## Run a sample

Run a sample from the root of a clone of the repository, with the .NET SDK installed. The command is in the table above;
a mode goes after `--`, as the next section shows.

```shell
git clone https://github.com/MarcelRoozekrans/Minos.NET.git
cd Minos.NET
dotnet run --project samples/Minos.NET.Samples.Guardrails
```

A recorded sample runs in one of three modes, chosen by one argument after `--`. No argument means replay. More than one
mode, or an argument that is not one, prints the usage and exits with code 2.

- **Replay** is the default, and `--replay` asks for it by name. It needs no network and no key. It answers every
  request from the sample's recordings, which are checked in.
- **`--live`** calls the API and prints the report. The single-provider samples talk to Jev through OpenRouter, so they
  need the `OPENROUTER_API_KEY` environment variable, and they make the billed requests that the table counts.
- **`--record`** does what `--live` does, and then rewrites the sample's recordings from the responses. It needs the key
  as well.

```shell
dotnet run --project samples/Minos.NET.Samples.Guardrails -- --live
```

The chat-client sample has no recordings and no modes. Without `OPENROUTER_API_KEY` it runs offline, with a scripted
chat client in place of the model; with the key it asks `openai/gpt-4.1-mini` through OpenRouter, or the model given
after `-- --model`. A test compares its offline output with a snapshot.

The provider-switch sample also takes `--provider <name>`, before or after the mode: `typesafe`, `openrouter` or
`local`. It overrides `Minos:Active` in the sample's `appsettings.json`. An unknown name exits with code 2 and lists the
configured providers. In the live modes it needs the key of the provider it calls: `TYPESAFE_API_KEY` for TypeSafe,
`OPENROUTER_API_KEY` for OpenRouter, and none for the local server, which must be running first, as
[Running a local server](providers.md#running-a-local-server) shows.

### Work from a clone for replay and record

Replay and record read and write the recordings in the sample's own source folder: `recordings.json`, or for the
provider-switch sample one file per provider in `recordings/`. A sample finds that folder by walking up from the running
program until it meets `Minos.NET.slnx`, the solution file at the root of the repository. So both modes work only where
that file is above the program, which means a clone, and not a build output that you copied somewhere else. Record
writes into the source folder on purpose, so that the next replay shows the new recording with no rebuild. `--live`
reads no recordings and runs from anywhere.

## The recordings

A recording is a file of real answers. The chat-client sample has none. The three single-provider samples hold real
OpenRouter answers from the model `typesafe/jev-1.13-20260917`, all recorded on 2026-10-02. Each file holds the
provider, the model, the date, and the response bodies, each filed under a hash of the request that earned it. The hash
is the SHA-256 of the exact request body. A recording never holds a header or a key.

The provider-switch sample keeps one file per provider in `recordings/`: `typesafe.json`, `openrouter.json` and
`local.json`. TypeSafe and OpenRouter receive byte-identical requests, so one file could not hold both. The billed Live
smoke workflow records the first two and the Local systemone workflow records `local.json`. Each uploads the files as an
artifact, and they reach the repository through a pull request. All three were recorded on 2026-10-13, on the 1.0 API:
`typesafe.json` from the model `jev-1.13.0`, `openrouter.json` from `typesafe/jev-1.13-20260917`, and `local.json` from
`Mapika/decider-2b-GGUF:Q4_K_M`.

Replay works by matching: the sample sends its request as usual, and a replacing handler hashes the body and returns the
recorded answer for that hash, without touching the network. This is the same idea as the canned handler on the
[testing page](testing-your-code.md#way-two-a-real-systemonedecisionclient-over-a-canned-http-reply), kept in a file.

It follows that a recording is only good for the request that made it. Change a sample's questions or its data, or
change the request format of the client in the core library, and the request body changes, its hash changes, and the
recording has no answer for it. Replay then fails with an error that names the sample and gives the command to record
it again. Record again with `--record`. A client change moves the requests of every sample, so it means recording all
of them.

A fresh recording is a fresh set of answers, and a borderline answer can land on the other side of a threshold. Read
the new report before you keep it.

## How CI keeps the samples correct

A sample that is not run goes stale, so the CI workflow checks them in three ways.

- **They are built with everything else.** The samples are part of the solution, so the build compiles them with
  warnings treated as errors.
- **Their tests run.** The sample tests check each policy and route with made-up answers, compare each sample's report
  with a checked-in snapshot, and check that every recording holds no key and no header, that every sample but the
  chat-client one has recordings, and that the provider-switch sample has one file per provider. The chat-client
  sample's test runs it offline and compares its whole output with a snapshot.
- **Each recorded sample runs, for real, in replay.** After the tests, a CI step runs every folder under `samples/` that
  holds a `recordings.json`, through the sample's real entry point: argument parsing, configuration and host setup
  included. It also runs the provider-switch sample once for each file in its `recordings/` folder, with `--provider`
  set to the file's name. A run passes only when the sample exits with code 0 and prints something. A new sample is
  picked up by adding its recordings. The step runs with empty `OPENROUTER_API_KEY` and `TYPESAFE_API_KEY`, so a sample
  that goes live by mistake fails at start-up and does not call the API.

So when a change to the library moves a request, CI fails with the message that names the sample and the command to
record it again, so a stale sample is caught by CI and not by a reader.

`samples/Minos.NET.AotSmoke` is also in that folder. It is not a cookbook sample: it is the Native AOT smoke app
that CI publishes and runs. [Native AOT](native-aot.md#where-it-is-checked) describes it.

## Next

- [Providers](providers.md): the presets, the capability matrix, and switching providers by configuration.
- [Performance](performance.md): what a call costs, measured.
- [Testing your code](testing-your-code.md): canned answers for your own tests.
- [Patterns](patterns/index.md): the same ideas in short, self-contained pages.
