# Minos.NET.Evaluation

Labelled decision datasets for [Minos.NET](https://www.nuget.org/packages/Minos.NET): a versioned JSONL format that pairs each item's state and questions with the answers you expect, a loader that reports every problem with its line number, a runner that sends the items through any `IDecisionClient`, and a calibration report built from the results.

```
dotnet add package Minos.NET.Evaluation
```

```csharp
using Minos;
using Minos.Evaluation;

var loaded = DecisionDataset.Load("support.jsonl");
if (loaded.IsFailure)
{
    // Every problem is listed, each with its 1-based line.
    foreach (var problem in loaded.Error) Console.WriteLine(problem);
    return;
}

var results = await DatasetRunner.RunAsync(client, loaded.Value, new DatasetRunOptions { DatasetName = "support" });
results.Save("results.jsonl");

var report = CalibrationReport.Create(results, loaded.Value, new PriceTable([]));
```

The package depends on [`Minos.NET.Recording`](https://www.nuget.org/packages/Minos.NET.Recording), for the export of recorded decisions. `DecisionDatasetExport.WriteAsync` turns recorded decisions with confirmed outcomes into a dataset in this format, and writes their signal outcomes to a companion file. From the command line it is `minos export --records decisions.jsonl`, and a `DatasetExportSummary` counts what was written and skipped.

`ShadowReport.CreateAsync` reads recorded decisions that shadow mode gave a candidate's answers, and compares the two providers: per question, how often they agree and a confusion table of their answers; per side, the calibration against the confirmed outcomes on the same questions, latency, cost and errors. `ToJson()` and `ToMarkdown()` write it, like the calibration report.

To run a dataset from the command line, without writing code, use the [`Minos.NET.Tool`](https://www.nuget.org/packages/Minos.NET.Tool) dotnet tool.

Documentation: [Labelled datasets](https://marcelroozekrans.github.io/Minos.NET/datasets). Source and issues: [github.com/MarcelRoozekrans/Minos.NET](https://github.com/MarcelRoozekrans/Minos.NET). Licensed under the MIT license.
