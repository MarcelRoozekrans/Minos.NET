using System.Globalization;
using System.Text;
using Minos.Evaluation;

namespace Minos.Samples.TicketTriage;

/// <summary>Report mode: reads the records, the dataset and the prices, and writes the two report files.</summary>
public static class TriageReportFiles
{
    /// <summary>The JSON report's file name.</summary>
    public const string JsonName = "triage-report.json";

    /// <summary>The Markdown report's file name.</summary>
    public const string MarkdownName = "triage-report.md";

    /// <summary>Builds the report and writes <c>triage-report.json</c> and <c>triage-report.md</c> into a directory, creating it.</summary>
    /// <param name="recordsPath">The records file.</param>
    /// <param name="datasetPath">The dataset file.</param>
    /// <param name="pricesPath">The prices file.</param>
    /// <param name="outDirectory">The directory to write to.</param>
    /// <exception cref="InvalidDataException">An input is unreadable or invalid, or a record id is unknown or duplicated; the message names the id and never a message text.</exception>
    public static void Write(string recordsPath, string datasetPath, string pricesPath, string outDirectory)
    {
        ArgumentNullException.ThrowIfNull(recordsPath);
        ArgumentNullException.ThrowIfNull(datasetPath);
        ArgumentNullException.ThrowIfNull(pricesPath);
        ArgumentNullException.ThrowIfNull(outDirectory);

        try
        {
            var records = TriageRecordReader.Read(recordsPath);
            var dataset = LoadDataset(datasetPath);
            var prices = PricesFile.Load(pricesPath);

            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var record in records)
            {
                if (!seen.Add(record.Id))
                {
                    throw new InvalidDataException("Record id '" + record.Id + "' appears more than once.");
                }
            }

            TriageReport report;
            try
            {
                report = TriageReportBuilder.Build(records, dataset, prices);
            }
            catch (InvalidOperationException ex)
            {
                throw new InvalidDataException(ex.Message, ex);
            }

            Directory.CreateDirectory(outDirectory);
            var utf8 = new UTF8Encoding(false);
            File.WriteAllText(Path.Combine(outDirectory, JsonName), TriageReportJsonWriter.Write(report), utf8);
            File.WriteAllText(Path.Combine(outDirectory, MarkdownName), TriageReportMarkdownWriter.Write(report), utf8);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InvalidDataException("A report file cannot be read or written: " + ex.Message.ReplaceLineEndings(" "), ex);
        }
    }

    private static DecisionDataset LoadDataset(string path)
    {
        var loaded = DecisionDataset.Load(path);
        if (loaded.IsSuccess)
        {
            return loaded.Value;
        }

        var lines = string.Join(", ", loaded.Error.Select(static p => p.Line.ToString(CultureInfo.InvariantCulture)));
        throw new InvalidDataException("The dataset '" + path + "' is invalid at line " + lines + ".");
    }
}
