# Minos.NET.Tool

The `minos` command line tool for [Minos.NET](https://www.nuget.org/packages/Minos.NET): calibrate a decision model against a labelled dataset, build the calibration report again from saved results, and check that a provider conforms to the decision protocol.

```
dotnet tool install --global Minos.NET.Tool
minos --help
```

The tool needs the .NET 10 runtime.

## Commands

| Command | What it does |
| --- | --- |
| `minos calibrate` | Runs a labelled dataset against a provider, then writes the results and a calibration report. |
| `minos report` | Builds the report again from saved results and their dataset, without calling a provider. |
| `minos conformance` | Sends the conformance probes to a live provider and checks its answers. |

Run `minos <command> --help` for the options of each. Without `--out`, a command writes into `minos-out/<provider>-<UTC time>` in the current directory.

```
minos calibrate --dataset support.jsonl --provider typesafe --prices prices.json
minos report --dataset support.jsonl --results minos-out/typesafe-20261017-120000/results.jsonl
minos conformance --provider typesafe
```

## The config file

The tool reads `minos.json` in the current directory, or the file given with `--config`. Each provider is a section under `Minos:Providers` with the options of `SystemOneDecisionClientOptions`, and `Minos:Active` names the one used when `--provider` is not given. Environment variables override the file, so a key need not be written down: `Minos__Providers__typesafe__ApiKey`.

```json
{
  "Minos": {
    "Active": "typesafe",
    "Providers": {
      "typesafe": { "Provider": "TypeSafe", "Model": "jev-1.14" },
      "local": { "Provider": "Custom", "BaseAddress": "http://localhost:8080/", "Path": "api/v1/systemone", "Model": "local-model" }
    }
  }
}
```

## The prices file

A provider that reports no cost can be priced with `--prices`. Each entry names the model, the price per million tokens, and where and when the price was read.

```json
{
  "prices": [
    { "model": "jev-1.14", "inputPerMillion": 0.042, "outputPerMillion": 0, "date": "2026-10-17", "source": "OpenRouter listing" }
  ]
}
```

## Exit codes

| Code | Meaning |
| --- | --- |
| 0 | The command did what it was asked. |
| 1 | A run-time failure: a stopped run, a failed check, a network fault or an output directory that cannot be written. |
| 2 | The input, the usage or the configuration is wrong. |
| 130 | The command was cancelled, by Ctrl+C or SIGTERM. A cancelled `minos calibrate` still saves the items that finished as `results.partial.jsonl`, with its report. |

Documentation: [Labelled datasets](https://marcelroozekrans.github.io/Minos.NET/datasets) and [the conformance suite](https://marcelroozekrans.github.io/Minos.NET/conformance). Source and issues: [github.com/MarcelRoozekrans/Minos.NET](https://github.com/MarcelRoozekrans/Minos.NET). Licensed under the MIT license.
