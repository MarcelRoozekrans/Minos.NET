---
id: datasets
title: Labelled datasets
sidebar_position: 7
description: The JSONL format for labelled decision items, the members of a line, how the loader reports problems, and the format version.
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

A criterion object, for a Choice option or a Score level, has the members `description`, `examples` and `not_for`.
`description` is required and is a string. `examples` and `not_for` are optional arrays of strings: the examples of the
option, and the things the option is not for. A criterion object holds text only. A JSON object or array as the
description of a Choice option or a Score level is not part of version 1, although the sides of a Noul may be JSON
values, as above.

The questions go through the same checks as a question set built at run time, so the rules of
[Question sets at run time](question-sets-at-run-time.md) apply, with the same `MIN` ids.

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

The decision export planned for Milestone 9 will write this format, and will use a new version if it needs members that
version 1 does not have.

## Running a dataset and reading the report

`DatasetRunner.RunAsync` sends every item of a dataset through an `IDecisionClient`. It makes one request per item, with
the item's own question set and state. Items run one at a time unless you set `MaxParallelism` on the
`DatasetRunOptions`. The results always come back in the order of the dataset. A request that fails with a
`DecisionError` does not stop the run: the failure is written on that item, and the run goes on to the next one. An
`OperationCanceledException` that your own token did not cause is written on that item as an `ItemError` of kind
`Cancelled`, and the run goes on too. A response whose answers do not fit the item turns the whole item into an
`InvalidResponse` error: an answer of the wrong kind, a chosen option or level the question does not have, a
probability or confidence outside 0 to 1, or no answer to any of the item's questions. These do stop the run: your own
cancellation token, which throws, and an exception thrown by the client or by the `Progress` receiver. In those cases
no new requests are sent, and requests already in flight are cancelled. Your own cancellation is thrown as it was, even
when it comes after a client fault, and then no partial results are returned. An exception from the client or the `Progress` receiver is thrown as the inner exception of a `DatasetRunException`, whose
`PartialResults` hold the items that finished, in dataset order, with `Header.Partial` set and `ItemCount` counting only
those. Only the first fault is kept. The cancelled in-flight requests are left out of the partial results, so any cost
the provider charged for them is in neither the results nor the report. The finished items were paid for, so save them. `CalibrationReport.Create` accepts them against the full dataset, as long as
every item is one of the dataset's and they keep its order, and the report says `Partial run: N of M items`. A results
file of a partial run has `"partial":true` in its header.

What you get back is an `EvaluationResults`: a `RunHeader` that names the run, and one `ItemResult` for each item. An
`ItemResult` holds the answers as `AnswerRecord` values, or an `ItemError` when the request failed, and also the latency,
the model that answered, the token counts and the cost. To build results by hand, make each answer with
`AnswerRecord.ForNoul`, `ForChoice` or `ForScore`; `ForNoul` takes the probability and works out the answer and its
confidence from it.

Save the results before you do anything else with them. A run costs real calls, and the results file is what it paid
for. `EvaluationResults.Save` writes it as JSONL, and `EvaluationResults.Load` reads it back with line-numbered
problems, as the dataset loader does, and refuses a member the format does not have. `Save` refuses results the file
cannot hold, such as a latency that is not a finite, non-negative number, and lists every problem. Because the file
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
(`ErrorSummary`), and the models that answered (`ModelCount`). A question is scored when it has an expected answer. A question without one is only counted, in the
report's header. A failed request, or an answer the provider left out, is an abstention: the question is scored but
unanswered. It is left out of accuracy, the calibration error, the bins and the base rate, but it counts as uncovered
in the coverage of the threshold sweep. A full guide to reading the numbers comes with a
later release.

## Next

- [Question sets at run time](question-sets-at-run-time.md): the builder and the rules every question goes through.
- [The client and its errors](client-and-errors.md): the `IDecisionClient` that the items run through.
- [Question types](question-types.md): what a Noul, a Choice and a Score answer holds.
