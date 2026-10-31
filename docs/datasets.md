---
id: datasets
title: Labelled datasets
sidebar_position: 9
description: The JSONL format for labelled decision items, the members of a line, how the loader reports problems, the format version, and the minos command line tool.
---

# Labelled datasets

A labelled dataset is a file of items with the right answers written next to them. Each item is a state to decide on,
the questions to ask about it and the answer you expect to every question. You can run the items through any
`IDecisionClient` and compare what comes back with what you expected.

The `Minos.NET.Evaluation` project reads the format. `DecisionDataset` loads a file, a stream or a string, and gives
back `DatasetItem` values that carry everything a call needs: the state, the question set and the expected answers.

## The format

A dataset is UTF-8 JSONL: one item per line, and blank lines are ignored. A line ends at `\n`, `\r\n` or `\r`, and a
byte order mark at the start of the file is skipped. Every line is complete in itself. It holds the
state, the questions and the expected answers, so the questions repeat on each line, and you can cut, shuffle or
concatenate a file without losing anything.

The example is one line. It is shown on a single line because JSONL needs that.

```json
{"id":"msg-1","state":"My card was meant to arrive last week and nothing has come.","questions":{"topic":{"type":"choice","instructions":"Which topic is this message about?","criteria":{"card_arrival":"The card has not arrived yet.","card_not_working":"The card arrived but does not work.","lost_or_stolen_card":null}},"urgent":{"type":"noul","instructions":"Does the customer need action today?"},"mood":{"type":"score","instructions":"How upset is the customer?","criteria":["calm","annoyed","angry"]}},"expected":{"topic":"card_arrival","urgent":false,"mood":1},"source":"support/2026-10#1","version":1}
```

## The members

| Member | Required | Content |
| --- | --- | --- |
| `id` | yes | A non-empty string, unique within the file. |
| `state` | yes | A string, or a JSON object or array for structured state. A number, boolean or `null` is a problem. |
| `questions` | yes | An object of questions keyed by question key, at least one. |
| `expected` | yes | The right answer for each question key, described below. Every key must name a question. A question with no expected answer is still sent, but it is not scored. |
| `source` | no | A string of free text that names where the item came from. |
| `version` | no | The format version. When it is present it must be `1`. |

Any other member is a problem. A typo such as `expect` is reported instead of being dropped without a word. A member
that appears twice in the same object is a problem too.

### Questions

Each question is an object with these members:

- `type` is `noul`, `choice` or `score`.
- `instructions` is a string, or a JSON object or array for structured instructions.
- `criteria` depends on the kind:
  - A Noul may have `{"true": ..., "false": ...}`, with either side left out. Each side is a string, or a JSON object
    or array, and it is read as content. It is not a criterion object.
  - A Choice needs an object of options, where each description is `null`, a string or a criterion object.
  - A Score needs an ordered list of levels, where each level is a string or a criterion object.

A criterion object, for a Choice option or a Score level, has either the members `description`, `examples` and
`not_for`, or the one member `json`.

- `description` is required and is a string. `examples` and `not_for` are optional arrays of strings: the examples of
  the option, and the things the option is not for. This form holds text only.
- `json` is a JSON object or array, which is sent as the criterion as it is, like `Criterion.Json`. It is the whole
  criterion: it cannot be combined with `description`, `examples` or `not_for`, so put anything else inside the JSON.
  For example `{"json": {"description": "Payments", "weight": 2}}` is a criterion that is that JSON.

The `json` form is an addition to version 1: a file without it stays valid.

The questions go through the same checks as a question set built at run time, so the rules of
[Question sets at run time](question-sets-at-run-time.md) apply, with the same `MIN` ids.

A [recorded decision](recording.md#the-jsonl-format) keeps its question set in its `questions` member in this same shape.

### Expected answers

The expected value follows the kind of question:

- A Noul takes a JSON boolean, `true` or `false`.
- A Choice takes the key of an option, as a string. The key must be one of the options.
- A Score takes the index of a level as an integer, counted from 0. The index must be inside the list of levels.

In code an expected answer is an `ExpectedAnswer`, made with `ExpectedAnswer.ForNoul`, `ExpectedAnswer.ForChoice` or
`ExpectedAnswer.ForScore`. It compares by value. `ForScore` refuses a negative level. An item's `Expected` is
read-only.

## Problems are values

A file with bad content does not throw. `DecisionDataset.Parse` and `DecisionDataset.Load` return a result that holds
either the dataset or the full list of problems, so you fix a file in one pass instead of one error at a time. The list
is a `DatasetProblems`, and each `DatasetProblem` has a 1-based `Line` and a `Message`.

A problem the core finds in a question starts with its `MIN` id, then the path of the question, for example
`MIN003 'questions.urgent': ...`. The core's warnings count as problems here too, all of them, because an item that breaks one is a broken item. MIN003 reports blank text, such as
blank instructions or a blank description. MIN005 reports a Score with fewer than two levels or more than 10, and a
Choice with more than 255 options.

A line that cannot be read as text is a problem on that line, and the lines after it are still read. Bytes that are
not valid UTF-8 are reported as "The line is not valid UTF-8." instead of being replaced. A string given to
`DecisionDataset.Parse` that holds an unpaired surrogate is reported as not valid Unicode. A line that is not valid
JSON names the byte in the line where the parser stopped, counted from 0, for example
`The line is not valid JSON at byte 11: ...`.

The loader throws only when the file does not exist or cannot be read.

## The version

The `version` member is optional. A line without it is read as version 1. A whole number other than 1 is a problem
that says: "This file uses dataset format version N; this loader reads version 1." Any other value, such as `"one"` or
`1.0`, is a problem that says: "'version' must be the number 1." A later version of the format will raise the number,
and a loader that does not know it refuses the file instead of guessing.

[Exporting recorded decisions](#exporting-recorded-decisions) writes this format, and uses a new version if it ever needs
members that version 1 does not have.

## Exporting recorded decisions

An app that [records its decisions](recording.md) can turn that traffic into a dataset. A decision that someone later
confirmed becomes an item: its state and questions, with the confirmed answers as the expected answers. The dataset is
then run like any other, through `DatasetRunner` or `minos calibrate`, against any provider, a local model included.

The input is a record file, the JSONL that `JsonlDecisionRecordSink` writes. An app with a sink of its own, such as a
database table, writes the same file from its rows with the public `DecisionRecordFile.WriteRecordLine`,
`WriteOutcomeLine` and `WriteErasureLine`; the [custom sink example](recording.md#writing-your-own-sink) shows it.
Read the file with `DecisionRecordFile.ReadAsync` and hand the decisions to `DecisionDatasetExport.WriteAsync`. It takes
an `IAsyncEnumerable<RecordedDecision>`, so you choose the decisions with LINQ before the export, by date or by question
set, and the export has no filter options of its own. It writes the dataset to a stream, and the signals to a second
stream, or to none when you pass `null`, in which case it only counts them.

The dataset a program writes this way is a copy of recorded state, so keep it as private as the record file, and
export again after an erasure, because an erasure does not reach an export made before it.

<!-- snippet: Datasets_Export -->
```cs
using Minos.Evaluation;
using Minos.Recording;

public static class DatasetExport
{
    // Turns the decisions of a record file that started on or after a date into a dataset and a signals file.
    public static async Task<DatasetExportSummary> ExportAsync(
        string recordsPath, DateTimeOffset since, string datasetPath, string signalsPath, CancellationToken cancellationToken)
    {
        // The export takes the decisions it is given, so you choose them first, here with a filter on the start time.
        var decisions = DecisionRecordFile.ReadAsync(recordsPath, cancellationToken)
            .Where(decision => decision.Record.Started >= since);

        await using var dataset = new FileStream(datasetPath, FileMode.Create, FileAccess.Write);
        await using var signals = new FileStream(signalsPath, FileMode.Create, FileAccess.Write);
        var summary = await DecisionDatasetExport.WriteAsync(decisions, dataset, signals, cancellationToken);

        // One line for each count that is not zero.
        Console.WriteLine(summary);
        return summary;
    }
}
```
<!-- endSnippet -->

To compare a second provider on the same traffic before you export anything, run it in
[shadow mode](recording.md#shadow-mode) and read [the shadow report](recording.md#the-shadow-report), or
`minos shadow-report`. It reads the same record file and needs no confirmed outcome for its agreement figures.

### What becomes an item

The export writes one line for each decision that has at least one valid confirmed answer, in the order the reader
returns the decisions. A line has these members. It has no `version`, so it reads as version 1.

| Member | Content |
| --- | --- |
| `id` | The decision id. The export writes each id once, so the ids are unique even when the decisions come from several files. |
| `state` | The recorded state, as it was written after redaction. |
| `questions` | The recorded question set, in the shape of a dataset line. |
| `expected` | The confirmed answers, merged as described below. |
| `source` | The distinct sources of the outcomes that supplied an expected answer, in file order, joined with `, `. |

A failed, a refused and a cached decision with a confirmed outcome is still an item, because its state and questions are
in the record. Two decisions on the same text become two items.

A decision is skipped, and counted by reason, when:

- it has no confirmed outcome;
- none of its confirmed answers is valid, after the checks below;
- it sent images. A record keeps only the hashes of the images, so the item cannot be rebuilt;
- its item would not load. The export checks each item with the loader's own rules before it writes it, so what it
  writes always loads with `DecisionDataset` and runs with `minos calibrate`. One way to reach this is a question set
  built at run time that only broke a core warning, since the loader counts warnings as problems. Another is a
  decision id that an earlier item already has. The reader keeps one decision for each id within a file, but decisions
  read from two files, one after the other, can repeat an id, and the first one wins.

A confirmed answer that does not fit its question is dropped, and counted, when:

- its key names no question of the record;
- its kind differs from the question's kind;
- it names a Choice option that the question does not have;
- it gives a Score level outside the question's levels.

### Several confirmed outcomes

A decision can have several confirmed outcomes: a correction, or confirmations from two sources that each cover some of
the questions. Each answer is checked first, and a dropped answer never takes part in the merge. Then each question key
takes its answer from the confirmed outcome with the latest `At`. When two have the same `At`, the one later in the file
wins. Partial confirmations combine, and a correction replaces what it corrects.

A key is counted as overridden when the winning answer differs from an earlier valid answer to the same key. It is
counted once for each item. A later outcome that repeats the same answer is not an override.

### The summary

`DecisionDatasetExport.WriteAsync` returns a `DatasetExportSummary`. It counts the decisions read, the items written,
the signals written and the signals skipped with their decision, the decisions skipped for each of the reasons above,
the confirmed keys dropped and the confirmed keys overridden. Its `ToString` prints one line for each count that is not
zero. It holds counts only. It never holds a decision id, an erasure key or any state, so it is safe to print in a log.

The export throws what the streams or the token throw, `OperationCanceledException` included, and the streams may hold
part of the output by then. The library deletes nothing, since it only has streams. `DecisionRecordFile.ReadAsync` reads
the whole file before it returns the first decision, so the export holds the file's decisions in memory.

### The signals file

A signal outcome is a fact about a decision, such as a customer who complained, and not an answer to a question. A
signal therefore never goes into the dataset. The dataset format rejects members it does not know, so the signals of the
exported items go to a second file, `dataset.signals.jsonl`, when you give the export a stream for it. It is UTF-8
JSONL, with one line for each signal of an exported item, in the item's order and then in file order. The signals of a
skipped decision are not written. They are counted as skipped.

```json
{"id":"0199f1d2-7c4e-7a10-9b3e-2f6d8c1a4e55","source":"mailer","at":"2026-10-22T10:30:00+00:00","name":"complained","value":1,"note":"Wrote back within the hour.","version":1}
```

| Member | Content |
| --- | --- |
| `id` | The dataset item id, which is the decision id. |
| `source` | The outcome's source. |
| `at` | The outcome's time, as an ISO 8601 date and time with its offset. |
| `name` | The signal's name. |
| `value` | The number, or left out when the signal has none. |
| `note` | The note, or left out when the signal has none. |
| `version` | `1`. |

Signals are weak labels. They show that something happened after a decision, not that the decision was right or wrong,
and they never become expected answers. Minos has no reader for this file yet.

### What the dataset leaves out

A dataset holds the right answers, not the answers the model gave. The recorded answers and their probability source, in
a version 2 record, are not exported, and a version 2 record exports the same as a version 1 record. Both come back in
the results when you run the dataset, and the format stays version 1.

### From the command line

`minos export` runs the same export on a file, without code. It makes no provider calls, so it needs no config or
prices.

```
minos export --records decisions.jsonl --out export
```

`--records` is required and names the record file. `--out` names the directory for the output; without it the command
uses `minos-out/export-<UTC yyyyMMdd-HHmmss>`, as [the other commands](#the-output) do. The command writes
`dataset.jsonl` and `dataset.signals.jsonl` there and prints their paths. The signals file is written even when it holds
no line, so a script can rely on it. A re-export into the same directory overwrites the earlier files, and one that
writes no item deletes them.

The dataset and the signals file are a copy of recorded state, so keep them as private as the record file. On Linux and
macOS the command creates them readable and writable by their owner only, as `JsonlDecisionRecordSink` creates the
record file. An erasure in the record file does not reach an export made before it: after an erasure, delete the earlier
exports and export again.

The command prints the problems of the record file as `line N: message`, then the summary, on standard error. A problem
in the record file, such as the cut last line a crash leaves, does not stop the export.

| Code | Meaning |
| --- | --- |
| 0 | At least one item was written. |
| 1 | The output cannot be written, or the export failed after it started writing. |
| 2 | No item was written, for any reason: the record file is missing or cannot be read, every line is a problem, or no decision can become an item. A missing or unreadable record file fails before any output is touched, so an earlier export stays. When the record file was read but gave no item, because every line is a problem or no decision can become an item, the command leaves no dataset or signals file and deletes those of an earlier export into the same directory. The directory stays, and so does the `.minos-run` marker of a default directory. |
| 130 | The export was cancelled. |

An export is cheap to repeat, unlike a billed calibrate run. So when it fails or is cancelled after it starts writing,
the tool deletes both output files and leaves no half-written dataset behind.

Run the exported dataset with `minos calibrate --dataset export/dataset.jsonl`.

## Running a dataset and reading the report

`DatasetRunner.RunAsync` sends every item of a dataset through an `IDecisionClient`. It makes one request per item, with
the item's own question set and state. Items run one at a time unless you set `MaxParallelism` on the
`DatasetRunOptions`. The results always come back in the order of the dataset. A request that fails with a
`DecisionError` does not stop the run: the failure is written on that item, and the run goes on to the next one. An
`OperationCanceledException` that your own token did not cause is written on that item as an `ItemError` of kind
`Cancelled`, and the run goes on too. A response whose answers do not fit the item turns the whole item into an
`InvalidResponse` error: an answer of the wrong kind, a chosen option or level the question does not have, a probability
or confidence outside 0 to 1, a `ProbabilitySource` that is not one of its named values, or no answer to any of the
item's questions. These do stop the run: your own cancellation token, and an exception thrown by the client or by the
`Progress` receiver. In those cases no new requests are sent, and requests still in flight are cancelled. Either way the
exception carries the items that finished, as `PartialResults`, in dataset order, with `Header.Partial` set and
`ItemCount` counting only those:

- Your own cancellation throws a `DatasetRunCanceledException`. It is an `OperationCanceledException` whose
  `CancellationToken` is your token, so `catch (OperationCanceledException e) when (e.CancellationToken == token)`
  still catches it. A token that is already cancelled sends nothing, and the exception holds no items.
- An exception from the client or the `Progress` receiver is thrown as the inner exception of a `DatasetRunException`.
  Only the first fault is kept.

Your cancellation wins over a fault: when your token is cancelled while a fault is stopping the run, you get the
`DatasetRunCanceledException`. A fault that came before your cancellation is its `InnerException`; one that comes after
it is dropped. In-flight requests that end cancelled are left out of the partial results, so any cost the provider
charged for them is not in the results or the report; a request that returns anyway is kept. The finished items were
paid for, so save them. `CalibrationReport.Create` accepts them against the full dataset, as long as every item is one
of the dataset's and they keep its order, and the report says `Partial run: N of M items`. A results file of a partial
run has `"partial":true` in its header.

What you get back is an `EvaluationResults`: a `RunHeader` that names the run, and one `ItemResult` for each item. An
`ItemResult` holds the answers as `AnswerRecord` values, or an `ItemError` when the request failed, and also the latency,
the model that answered, the token counts and the cost. Each `AnswerRecord` keeps the `ProbabilitySource` of the answer
it came from: `Model`, `Logprobs` or `Verbalized`, as [chat clients](chat-clients.md) describe. To build results by hand,
make each answer with `AnswerRecord.ForNoul`, `ForChoice` or `ForScore`; `ForNoul` takes the probability and works out
the answer and its confidence from it. Each factory has an overload that also takes the `ProbabilitySource`; without
it, the source is `Model`.

Save the results before you do anything else with them. A run costs real calls, and the results file is what it paid
for. `EvaluationResults.Save` writes it as JSONL, and `EvaluationResults.Load` reads it back with line-numbered
problems, as the dataset loader does, and refuses a member the format does not have. `Save` refuses results the file
cannot hold, such as a latency that is not a finite, non-negative number, and lists every problem. An answer whose
source is not `Model` has a `"probabilitySource"` member, `"logprobs"` or `"verbalized"`. A `Model` answer has none, so
a file with only `Model` answers is the same as one an older version wrote, and an answer without the member reads as
`Model`. Any other value is a problem. The file stays version 1: an older reader refuses the member as unknown, so it
cannot lose a source without saying so. Because the file
holds every answer, you can compute the report again, with new prices or after you correct an expected answer, without
making another billed call.

A `CalibrationReport` is made from the results and the dataset they came from. The prices are optional: a `PriceTable`
lists a `ModelPrice` for each model. A `ModelPrice` holds the model, the price per million input tokens, the price per
million output tokens, the date of the price and its source. The example uses an output price of 0, which is only an
example value; use the prices your provider lists.

The cost of an item is decided in this order:

1. The cost the provider reported, when there is one. A reported cost of 0 counts as reported.
2. Otherwise both token counts times a matching price from the table.
3. Otherwise it is unknown. A missing token count, or a response with no model, makes the cost unknown.

A price matches a model when its model is equal to the response's model id, or when the response's model id starts with
the price's model followed by `-`. The longest such entry wins, so `jev-1.14` matches `jev-1.14-20261014`.

Responses that came from a cache are left out of the latency and the cost, and are counted separately. An
`InvalidResponse` item was served and billed, so its cost counts in the cost and its model among the models that
answered; like every failed request, it is left out of the latency and the answers.

<!-- snippet: DatasetRun_RunAndReport -->
```cs
using Minos;
using Minos.Evaluation;

public static class DatasetRun
{
    // Runs every item of a dataset file, keeps the results, and builds the report from them.
    public static async Task<CalibrationReport> RunAndReportAsync(IDecisionClient client, string datasetPath, string resultsPath)
    {
        var loaded = DecisionDataset.Load(datasetPath);
        if (loaded.IsFailure)
        {
            throw new InvalidOperationException("The dataset has problems: " + string.Join("; ", loaded.Error.Select(problem => problem.ToString())));
        }

        var dataset = loaded.Value;

        // One request per item. The default is one at a time; MaxParallelism lets several run together.
        var results = await DatasetRunner.RunAsync(client, dataset, new DatasetRunOptions { DatasetName = "support", MaxParallelism = 4 });

        // The results file holds what the run paid for. Save it before anything else.
        results.Save(resultsPath);

        // The prices are only used for responses that report no cost of their own.
        var price = new ModelPrice
        {
            Model = "jev-1.14",
            InputPerMillion = 0.042,
            OutputPerMillion = 0,
            Date = new DateOnly(2026, 10, 17),
            Source = "OpenRouter listing",
        };
        var prices = new PriceTable([price]);

        // Later, EvaluationResults.Load(resultsPath) brings the same results back, and the report is made again from them.
        var report = CalibrationReport.Create(results, dataset, prices);
        if (report.IsFailure)
        {
            throw new InvalidOperationException("The results do not fit the dataset: " + string.Join("; ", report.Error.Select(problem => problem.ToString())));
        }

        File.WriteAllText("report.json", report.Value.ToJson());
        File.WriteAllText("report.md", report.Value.ToMarkdown());
        return report.Value;
    }
}
```
<!-- endSnippet -->

`CalibrationReport.Create` returns a result. It fails with problems when the results do not fit the dataset, for example
when they were made from a different file, or when they are results `Save` would refuse. Each problem's line is the
line of its item in the dataset file. A problem with the results header has line 0, and a results item past the
dataset's last item has the line after that item, or 1 when the dataset is empty. `ToJson` writes the report as JSON
of kind `minos-report`, and `ToMarkdown` writes it as a page to read.

The report holds, for each kind of question and for all of them together (a `KindMetrics`):

- accuracy, and the base rate, the accuracy you would get by always giving the most common expected answer. The base
  rate is worked out per question, over the answered questions only, from the same answers it is compared with, so it
  is high when a question has few answers: ten items that each ask their question under a key of its own give a base
  rate of 1, since every question's one answer is its own majority;
- the expected calibration error, as the headline figure with equal-mass bins and again with equal-width bins, and the
  `ReliabilityBin` list behind it;
- a threshold sweep (`SweepPoint`): for each confidence threshold, how many of the questions are answered at or above
  it, and how accurate those answers are. A Noul answer counts when its confidence reaches the threshold, or when its
  probability p does in either direction, so p = 0.07 counts at 0.93 just as p = 0.93 does.

It also holds the latency (`LatencySummary`), the cost per 1,000 decisions (`CostSummary`), the errors by kind
(`ErrorSummary`), and the models that answered (`ModelCount`). `VerbalizedAnswers` counts the scored answers whose
probabilities are `Verbalized`: the model stated them in its text, so they are not a measured distribution. They are
scored like any other answer, so the calibration figures include them. When there are some, the Markdown report says how
many, and the JSON has a `verbalizedAnswers` member; when there are none, neither changes. A question is scored when it
has an expected answer. A question without one is only counted, in the report's header. A failed request, or an answer
the provider left out, is an abstention: the question is scored but unanswered. It is left out of accuracy, the
calibration error, the bins and the base rate, but it counts as uncovered in the coverage of the threshold sweep.
[Choosing a provider and a threshold](choosing-a-provider.md) reads these numbers in real reports.

## Running it from the command line

The `minos` tool runs the same steps without any code: it calibrates a model against a dataset, builds the report again
from saved results, runs the [conformance suite](conformance.md) against a provider, and [exports recorded decisions](#from-the-command-line) to a dataset. It needs the .NET 10 runtime.

```
dotnet tool install --global Minos.NET.Tool
minos --help
```

### The config file

The tool reads `minos.json` in the current directory, or the file given with `--config`. Each provider is a section
under `Minos:Providers`, named as you like, with the options of `SystemOneDecisionClientOptions`: `Provider`, `Model`,
`BaseAddress`, `Path`, `MaxRetries` and the rest of the [client's options](client-and-errors.md). `Minos:Active` names
the section used when you do not pass `--provider`.

<!-- minos.json -->
```json
{
  "Minos": {
    "Active": "TypeSafe",
    "Providers": {
      "TypeSafe": { "Provider": "TypeSafe" },
      "OpenRouter": { "Provider": "OpenRouter", "Model": "cloudflare/clef-flash" },
      "local": { "Provider": "Custom", "BaseAddress": "http://localhost:8000/", "Model": "local-model" }
    }
  }
}
```

Environment variables are read after the file and win over it, so `Minos__Providers__OpenRouter__Model` replaces the
OpenRouter section's `Model`. The `--model` option wins over both. The API keys come from the environment, as the client
reads them: `TYPESAFE_API_KEY` and `OPENROUTER_API_KEY`. You can also write a key in the file as `ApiKey`, but then
keep the file out of source control. A hosted provider without a key, or a `Custom` provider without a `BaseAddress` or
a `Model`, stops the command with exit code 2 before it sends anything.

### The prices file

A provider that reports a cost needs no prices. For one that does not, pass a prices file with `--prices`. It holds the
entries of a `PriceTable`: the model, the price per million input and output tokens, and the date and source of the
price. Cost is decided in the order described under
[Running a dataset and reading the report](#running-a-dataset-and-reading-the-report), and the date must be written as
`yyyy-MM-dd`.

<!-- prices.json -->
```json
{
  "prices": [
    { "model": "jev-1.14", "inputPerMillion": 0.042, "outputPerMillion": 0, "date": "2026-10-17", "source": "OpenRouter listing" }
  ]
}
```

### The commands

`minos calibrate` runs a dataset through a provider and writes the results and the report. `--dataset` is required.
`--parallel` sets how many items run together and defaults to 1; the other options are `--config`, `--provider`,
`--model`, `--prices` and `--out`.

```
minos calibrate --dataset support.jsonl --provider OpenRouter --parallel 4 --prices prices.json
```

`minos report` builds the report again from the results of an earlier run and the dataset they came from. It calls no
provider, so it costs nothing, and it is how you apply new prices or a corrected expected answer. It takes `--results`
and `--dataset`, both required, and the options `--prices` and `--out`.

```
minos report --results minos-out/OpenRouter-20261017-120000/results.jsonl --dataset support.jsonl --prices prices.json
```

`minos conformance` sends the conformance probes to a live provider and checks its answers. It takes `--config`,
`--provider`, `--model` and `--out`. Every probe is a real request, and the provider may bill it. The `bad-key` probe
needs a key to make invalid, so it is skipped for a provider that needs no key, such as a local server.

```
minos conformance --provider local
```

`minos <command> --help` lists the options of a command.

### The output

Without `--out`, a command writes into `minos-out/<provider>-<UTC yyyyMMdd-HHmmss>` in the current directory, where
`<provider>` is the provider's section name. When that directory exists, the name ends in `-2`, then `-3`, and so on.
For `minos report` the provider is the one in the results file. The command creates the directory when it is missing,
before the first request, so a directory that cannot be written fails the command before any call is paid for. A
default directory also holds an empty `.minos-run` file, which marks it as taken by one run. A run that is cancelled or
fails before it writes anything leaves that directory holding only the marker, and it can be deleted. The command prints
the path of each file it writes.

| Command | Files |
| --- | --- |
| `minos calibrate` | `results.jsonl`, `report.json` and `report.md`. |
| `minos report` | `report.json` and `report.md`. |
| `minos conformance` | `conformance.json` and `conformance.md`. |
| `minos export` | `dataset.jsonl` and `dataset.signals.jsonl`. |

`results.jsonl` is what `EvaluationResults.Save` writes, and `report.json` and `report.md` are the `ToJson` and
`ToMarkdown` of the report. `minos conformance` also prints how many checks passed, failed and were skipped.

### Exit codes

| Code | Meaning |
| --- | --- |
| 0 | The command did what it was asked. A skipped check counts as a pass. |
| 1 | A run-time failure: a run that stopped, a failed conformance check, a network fault, or an output directory that cannot be written. |
| 2 | The input, the usage or the configuration is wrong, such as a dataset with problems, a missing config file or an unknown provider. |
| 130 | The command was cancelled: by Ctrl+C or Ctrl+Break, or on Unix by `Ctrl+\` or SIGTERM. A cancelled `minos calibrate` still saves the items that finished. |

A failure prints one line on stderr. The problems of a dataset or a results file are printed one per line, as
`line N: message`, so you can fix them in one pass.

### A run that stops

When the client throws during `minos calibrate`, or the run is cancelled, the tool does not lose what the finished items
cost. It writes the finished items to `results.partial.jsonl` instead of `results.jsonl`, builds `report.json` and
`report.md` from them, and prints their paths. A run the client stopped exits with code 1; a cancelled run exits with
code 130. The report reads `Partial run: N of M items`. It deletes a `results.jsonl` left in the same directory by an
earlier run, so the directory never holds two contradicting runs. A partial results file works with `minos report`,
together with the full dataset. To finish, run the dataset again.

The run is cancelled by Ctrl+C. On Windows Ctrl+Break cancels it too; on Unix, `Ctrl+\` and SIGTERM do. Windows maps
SIGTERM to a system shutdown, which only a service sees, so there only Ctrl+C and Ctrl+Break save the run. Every
command then prints `Stopping; press Ctrl+C again to quit at once.`, and `minos calibrate` writes the files, which
takes a moment. A second Ctrl+C, or any second signal, ends the tool at once, without saving. A run cancelled before
any item finished writes nothing and leaves the files of an earlier run in place.

In CI, the signal has to reach the tool. A GitHub Actions job that hits its timeout or is cancelled signals only the
step's shell on Linux and macOS: SIGINT, then SIGTERM 7.5 seconds later, then a kill. The repository's calibration
workflows run `minos calibrate` in the background and pass the stop on with a `trap`:

```bash
trap 'trap "" INT TERM; if [ -n "$pid" ]; then kill -TERM "$pid" 2>/dev/null; wait "$pid"; fi; exit 130' INT TERM
minos calibrate --dataset data.jsonl --out out & pid=$!
wait "$pid"
```

SIGTERM is forwarded, not SIGINT, because a background job ignores SIGINT. On a Windows runner, Ctrl+C and Ctrl+Break
reach every process in the console, so the tool gets them directly.

## Next

- [Choosing a provider and a threshold](choosing-a-provider.md): the reports of three providers, read side by side.
- [Question sets at run time](question-sets-at-run-time.md): the builder and the rules every question goes through.
- [The client and its errors](client-and-errors.md): the `IDecisionClient` that the items run through.
- [Question types](question-types.md): what a Noul, a Choice and a Score answer holds.
- [Conformance](conformance.md): the suite that `minos conformance` runs.
