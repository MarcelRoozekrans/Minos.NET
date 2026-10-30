namespace Minos.Docs.Tests;

#region Datasets_Export
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
#endregion
