using System.Buffers;
using System.Globalization;
using System.Text;

namespace Minos.Evaluation;

// One thing the results file cannot hold: about the header when Index is null, otherwise about the item at Index.
internal readonly record struct ResultsProblem(int? Index, string Message);

// The value rules of the results file, shared by the writer, the reader and the report, so that what Save accepts,
// Load accepts, and what the report computes from is what a file could hold.
internal static class ResultsRules
{
    // Every problem of results built by hand, in header then item order. An AnswerRecord is valid by construction,
    // through its factories, so only its presence and its text are checked here.
    internal static List<ResultsProblem> Check(EvaluationResults results)
    {
        var problems = new List<ResultsProblem>();
        CheckHeader(results.Header, results.Items.Count, problems);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < results.Items.Count; i++)
        {
            CheckItem(i, results.Items[i], ids, problems);
        }

        return problems;
    }

    private static void CheckHeader(RunHeader header, int count, List<ResultsProblem> problems)
    {
        void Add(string message) => problems.Add(new ResultsProblem(null, "Header: " + message));

        if (string.IsNullOrEmpty(header.DatasetName))
        {
            Add("DatasetName must not be null or empty.");
        }

        if (header.ItemCount != count)
        {
            Add(string.Create(CultureInfo.InvariantCulture, $"ItemCount is {header.ItemCount}, but there are {count} items."));
        }

        if (header.Parallelism < MinParallelism)
        {
            Add(string.Create(CultureInfo.InvariantCulture, $"Parallelism must be at least {MinParallelism}."));
        }

        Text(Add, "DatasetName", header.DatasetName);
        Text(Add, "ProviderName", header.ProviderName);
        Text(Add, "Endpoint", header.Endpoint?.OriginalString);
        Text(Add, "DefaultModel", header.DefaultModel);
    }

    private static void CheckItem(int index, ItemResult? item, HashSet<string> ids, List<ResultsProblem> problems)
    {
        var position = (index + 1).ToString(CultureInfo.InvariantCulture);
        if (item is null)
        {
            problems.Add(new ResultsProblem(index, $"Item {position} is null."));
            return;
        }

        var owner = item.Id is null ? $"Item {position}" : $"Item {position} '{item.Id}'";
        void Add(string message) => problems.Add(new ResultsProblem(index, owner + ": " + message));

        if (item.Id is null)
        {
            Add("Id must be set.");
        }
        else if (!ids.Add(item.Id))
        {
            Add("the id appears twice.");
        }

        Text(Add, "Id", item.Id);
        Text(Add, "Model", item.Model);
        Text(Add, "ResponseId", item.ResponseId);
        Require(Add, "Line", item.Line >= MinLine, string.Create(CultureInfo.InvariantCulture, $"at least {MinLine}"));
        Require(Add, "LatencyMs", IsNonNegative(item.LatencyMs), "a non-negative finite number");
        Require(Add, "Cost", item.Cost is not { } cost || IsNonNegative(cost), "a non-negative finite number, or null when unknown");
        Require(Add, "InputTokens", item.InputTokens is not { } input || input >= MinTokens, "non-negative");
        Require(Add, "OutputTokens", item.OutputTokens is not { } output || output >= MinTokens, "non-negative");

        if ((item.Answers is null) == (item.Error is null))
        {
            Add("exactly one of Answers and Error must be set.");
        }

        if (item.Error is { } error)
        {
            Require(Add, "Error.Kind", error.Kind is not null, "set");
            Require(Add, "Error.Message", error.Message is not null, "set");
            Text(Add, "Error.Kind", error.Kind);
            Text(Add, "Error.Message", error.Message);
        }

        if (item.Answers is not { } answers)
        {
            return;
        }

        Require(Add, "Answers", answers.Count > 0, "not empty");
        foreach (var (key, answer) in answers)
        {
            var member = $"Answers[{key}]";
            Text(Add, member + " key", key);
            Require(Add, member, answer is not null, "set, not null");
            Text(Add, member + ".ChoiceKey", answer?.ChoiceKey);
        }
    }

    private static void Text(Action<string> add, string member, string? text)
    {
        if (!IsValidText(text))
        {
            add(member + " holds an unpaired surrogate and cannot be written as UTF-8.");
        }
    }

    private static void Require(Action<string> add, string member, bool ok, string rule)
    {
        if (!ok)
        {
            add($"{member} must be {rule}.");
        }
    }

    internal static bool IsUnit(double value) => double.IsFinite(value) && value is >= 0 and <= 1;

    // Null is fine here: whether a member may be null is its own rule. Text with an unpaired surrogate cannot be written as UTF-8.
    internal static bool IsValidText(string? text)
    {
        var rest = text.AsSpan();
        while (!rest.IsEmpty)
        {
            if (Rune.DecodeFromUtf16(rest, out _, out var used) != OperationStatus.Done)
            {
                return false;
            }

            rest = rest[used..];
        }

        return true;
    }

    // Replaces each unpaired surrogate with U+FFFD, for text a provider supplied.
    internal static string? Clean(string? text)
    {
        if (text is null || IsValidText(text))
        {
            return text;
        }

        var builder = new StringBuilder(text.Length);
        var rest = text.AsSpan();
        while (!rest.IsEmpty)
        {
            var rune = Rune.DecodeFromUtf16(rest, out var value, out var used) == OperationStatus.Done ? value : Rune.ReplacementChar;
            builder.Append(rune.ToString());
            rest = rest[used..];
        }

        return builder.ToString();
    }

    // A Noul answer's flag and confidence are derived from its probability, as the runner writes them: the flag is
    // p >= 0.5 and the confidence is max(p, 1 - p). A saved answer that disagrees would score accuracy from one
    // number and calibration from another.
    internal const double NoulConfidenceTolerance = 1e-9;

    internal static bool IsConsistentNoul(bool value, double probability, double confidence)
        => value == (probability >= 0.5) && Math.Abs(confidence - Math.Max(probability, 1 - probability)) <= NoulConfidenceTolerance;

    internal const string NoulConsistencyRule = "agree with its probability: the value is probability >= 0.5 and the confidence is max(p, 1 - p)";

    internal static bool IsNonNegative(double value) => double.IsFinite(value) && value >= 0;

    // The smallest allowed values of the integer members.
    internal const int MinItemCount = 0;
    internal const int MinParallelism = 1;
    internal const int MinLine = 1;
    internal const int MinTokens = 0;
    internal const int MinScoreLevel = 0;
}
