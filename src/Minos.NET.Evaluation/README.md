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

The package depends on [`Minos.NET.Recording`](https://www.nuget.org/packages/Minos.NET.Recording), for the export of recorded decisions.

To run a dataset from the command line, without writing code, use the [`Minos.NET.Tool`](https://www.nuget.org/packages/Minos.NET.Tool) dotnet tool.

Documentation: [Labelled datasets](https://marcelroozekrans.github.io/Minos.NET/datasets). Source and issues: [github.com/MarcelRoozekrans/Minos.NET](https://github.com/MarcelRoozekrans/Minos.NET). Licensed under the MIT license.
