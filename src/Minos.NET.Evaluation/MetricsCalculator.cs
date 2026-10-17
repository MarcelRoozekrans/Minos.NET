using System.Diagnostics;
using System.Globalization;

namespace Minos.Evaluation;

// Computes a calibration report from results and their dataset. The metric definitions are those of the Phase 8.2
// design: answered questions feed accuracy, ECE, bins and base rate; scored questions are the sweep's denominator.
internal static class MetricsCalculator
{
    private const int MaxBins = 10;
    private const int SweepPoints = 100;
    private const double TokensPerMillion = 1_000_000;

    // One scored question. Order is its position in item order, then the item's question order.
    private readonly record struct ScoredQuestion(QuestionKind Kind, string Key, ExpectedAnswer Expected, bool Answered, bool Correct, double Confidence, int Order);

    internal static DatasetProblems? Match(EvaluationResults results, DecisionDataset dataset)
    {
        var problems = new List<DatasetProblem>();
        var header = results.Header.ItemCount;
        if (header != dataset.Count)
        {
            problems.Add(new DatasetProblem(
                Math.Min(header, dataset.Count) + 1,
                string.Create(CultureInfo.InvariantCulture, $"The results header has {header} items, but the dataset has {dataset.Count}.")));
        }

        var count = results.Items.Count;
        if (count != dataset.Count)
        {
            problems.Add(new DatasetProblem(
                Math.Min(count, dataset.Count) + 1,
                string.Create(CultureInfo.InvariantCulture, $"The results have {count} items, but the dataset has {dataset.Count}.")));
        }

        var shared = Math.Min(count, dataset.Count);
        for (var i = 0; i < shared; i++)
        {
            var id = results.Items[i].Id;
            var expected = dataset[i].Id;
            if (!string.Equals(id, expected, StringComparison.Ordinal))
            {
                problems.Add(new DatasetProblem(
                    i + 1,
                    string.Create(CultureInfo.InvariantCulture, $"Item {i + 1} of the results is '{id}', but the dataset's is '{expected}'.")));
            }
        }

        for (var i = 0; i < shared; i++)
        {
            CheckAnswers(i + 1, results.Items[i], dataset[i], problems);
        }

        return problems.Count == 0 ? null : new DatasetProblems([.. problems]);
    }

    // Every saved answer must fit the item's own question. A missing answer is not a problem: the runner leaves out
    // the answers a provider omitted, and those questions count as scored but unanswered.
    private static void CheckAnswers(int line, ItemResult result, DatasetItem item, List<DatasetProblem> problems)
    {
        if (result.Error is not null || result.Answers is null)
        {
            return;
        }

        var keys = result.Answers.Keys.ToList();
        keys.Sort(StringComparer.Ordinal);
        foreach (var key in keys)
        {
            var answer = result.Answers[key];
            var problem = Check(item, key, answer);
            if (problem is not null)
            {
                problems.Add(new DatasetProblem(line, string.Create(CultureInfo.InvariantCulture, $"Item {line} '{item.Id}': {problem}")));
            }
        }
    }

    private static string? Check(DatasetItem item, string key, AnswerRecord answer)
    {
        var question = FindQuestion(item.Definition, key);
        if (question is null)
        {
            return $"the answer '{key}' is not a question of the item.";
        }

        if (answer.Kind != question.Kind)
        {
            return $"the answer '{key}' is a {answer.Kind} answer, but the question is a {question.Kind}.";
        }

        if (!ResultsRules.IsUnit(answer.Confidence))
        {
            return $"the answer '{key}' has a confidence that is not a number from 0 to 1.";
        }

        return question.Kind switch
        {
            QuestionKind.Noul when answer.Probability is not { } p || !ResultsRules.IsUnit(p)
                => $"the answer '{key}' has a probability that is not a number from 0 to 1.",
            QuestionKind.Noul when !ResultsRules.IsConsistentNoul(answer.Noul, answer.Probability!.Value, answer.Confidence)
                => $"the answer '{key}' does not {ResultsRules.NoulConsistencyRule}.",
            QuestionKind.Choice when !HasOption(question, answer.ChoiceKey)
                => $"the answer '{key}' chose '{answer.ChoiceKey}', which is not an option of the question.",
            QuestionKind.Score when answer.ScoreLevel < 0 || answer.ScoreLevel >= question.Options.Count
                => string.Create(CultureInfo.InvariantCulture, $"the answer '{key}' chose level {answer.ScoreLevel}, but the question has {question.Options.Count} levels."),
            _ => null,
        };
    }

    private static QuestionDefinition? FindQuestion(QuestionSetDefinition definition, string key)
    {
        foreach (var question in definition.Questions)
        {
            if (string.Equals(question.Key, key, StringComparison.Ordinal))
            {
                return question;
            }
        }

        return null;
    }

    private static bool HasOption(QuestionDefinition question, string? key)
    {
        foreach (var option in question.Options)
        {
            if (string.Equals(option.Key, key, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    internal static CalibrationReport Compute(EvaluationResults results, DecisionDataset dataset, PriceTable? prices)
    {
        var questions = Collect(results, dataset, out var unscored);
        return new CalibrationReport(
            results.Header,
            ForKind(questions, QuestionKind.Noul),
            ForKind(questions, QuestionKind.Choice),
            ForKind(questions, QuestionKind.Score),
            Metrics(questions),
            unscored,
            Latency(results.Items),
            Cost(results.Items, prices),
            Errors(results.Items),
            Models(results.Items));
    }

    private static List<ScoredQuestion> Collect(EvaluationResults results, DecisionDataset dataset, out int unscored)
    {
        var questions = new List<ScoredQuestion>();
        unscored = 0;
        for (var i = 0; i < dataset.Count; i++)
        {
            var item = dataset[i];
            var result = results.Items[i];
            foreach (var question in item.Definition.Questions)
            {
                if (!item.Expected.TryGetValue(question.Key, out var expected))
                {
                    unscored++;
                    continue;
                }

                AnswerRecord? answer = null;
                var answered = result.Error is null && result.Answers is not null && result.Answers.TryGetValue(question.Key, out answer);
                questions.Add(new ScoredQuestion(
                    expected.Kind,
                    question.Key,
                    expected,
                    answered,
                    answered && IsCorrect(expected, answer!),
                    answered ? answer!.Confidence : 0,
                    questions.Count));
            }
        }

        return questions;
    }

    // Match has checked that the answer's kind is its question's, and the dataset that the expected answer's is.
    private static bool IsCorrect(ExpectedAnswer expected, AnswerRecord answer)
    {
        Debug.Assert(answer.Kind == expected.Kind, "Match refuses an answer whose kind is not its question's.");
        return expected.Kind switch
        {
            QuestionKind.Noul => answer.Noul == expected.Noul,
            QuestionKind.Choice => string.Equals(answer.ChoiceKey, expected.ChoiceKey, StringComparison.Ordinal),
            QuestionKind.Score => answer.ScoreLevel == expected.ScoreLevel,
            _ => throw new UnreachableException(),
        };
    }

    private static KindMetrics? ForKind(List<ScoredQuestion> questions, QuestionKind kind)
    {
        var ofKind = questions.FindAll(q => q.Kind == kind);
        return ofKind.Count == 0 ? null : Metrics(ofKind);
    }

    private static KindMetrics Metrics(List<ScoredQuestion> scored)
    {
        var answered = scored.FindAll(q => q.Answered);
        answered.Sort(static (a, b) => a.Confidence != b.Confidence ? a.Confidence.CompareTo(b.Confidence) : a.Order.CompareTo(b.Order));
        var sweep = Sweep(answered, scored.Count);
        if (answered.Count == 0)
        {
            return new KindMetrics(scored.Count, 0, null, null, null, null, [], sweep);
        }

        var n = answered.Count;
        var correct = answered.Count(q => q.Correct);
        var bins = EqualMassBins(answered);
        return new KindMetrics(
            scored.Count,
            n,
            (double)correct / n,
            BaseRate(answered),
            CalibrationError(bins, n),
            CalibrationError(EqualWidthBins(answered), n),
            bins,
            sweep);
    }

    // What answering each question's most common expected answer, every time, would score: per question key, the
    // count of its most common expected answer, summed and divided by the answered count. For a Noul question that is
    // the larger of its true and false counts. Questions are told apart by kind and key, since options differ per question.
    private static double BaseRate(List<ScoredQuestion> answered)
    {
        var counts = new Dictionary<(QuestionKind Kind, string Key, ExpectedAnswer Expected), int>();
        foreach (var question in answered)
        {
            var group = (question.Kind, question.Key, question.Expected);
            counts[group] = counts.GetValueOrDefault(group) + 1;
        }

        var mostCommon = new Dictionary<(QuestionKind Kind, string Key), int>();
        foreach (var (group, count) in counts)
        {
            var question = (group.Kind, group.Key);
            mostCommon[question] = Math.Max(mostCommon.GetValueOrDefault(question), count);
        }

        return (double)mostCommon.Values.Sum() / answered.Count;
    }

    // Answered questions are already sorted by confidence, then order. The first n % 10 bins hold one extra answer.
    private static ReliabilityBin[] EqualMassBins(List<ScoredQuestion> sorted)
    {
        var n = sorted.Count;
        var binCount = Math.Min(MaxBins, n);
        var size = n / binCount;
        var extra = n % binCount;
        var bins = new ReliabilityBin[binCount];
        var start = 0;
        for (var b = 0; b < binCount; b++)
        {
            var length = size + (b < extra ? 1 : 0);
            bins[b] = Bin(sorted.GetRange(start, length));
            start += length;
        }

        return bins;
    }

    private static List<ReliabilityBin> EqualWidthBins(List<ScoredQuestion> answered)
    {
        var buckets = new List<ScoredQuestion>[MaxBins];
        foreach (var question in answered)
        {
            var index = Math.Min((int)(question.Confidence * MaxBins), MaxBins - 1);
            (buckets[index] ??= []).Add(question);
        }

        var bins = new List<ReliabilityBin>(MaxBins);
        foreach (var bucket in buckets)
        {
            if (bucket is not null)
            {
                bins.Add(Bin(bucket));
            }
        }

        return bins;
    }

    private static ReliabilityBin Bin(List<ScoredQuestion> questions)
        => new(questions.Count, questions.Sum(q => q.Confidence) / questions.Count, (double)questions.Count(q => q.Correct) / questions.Count);

    private static double CalibrationError(IReadOnlyList<ReliabilityBin> bins, int n)
        => bins.Sum(b => b.Count * Math.Abs(b.Accuracy - b.MeanConfidence)) / n;

    private static SweepPoint[] Sweep(List<ScoredQuestion> answered, int scored)
    {
        var points = new SweepPoint[SweepPoints];
        for (var i = 0; i < SweepPoints; i++)
        {
            var threshold = i / 100.0;
            var covered = 0;
            var correct = 0;
            foreach (var question in answered)
            {
                if (question.Confidence >= threshold)
                {
                    covered++;
                    correct += question.Correct ? 1 : 0;
                }
            }

            points[i] = new SweepPoint(
                threshold,
                scored == 0 ? 0 : (double)covered / scored,
                covered == 0 ? null : (double)correct / covered,
                covered);
        }

        return points;
    }

    private static LatencySummary Latency(IReadOnlyList<ItemResult> items)
    {
        var latencies = items.Where(r => r.Error is null && !r.Cached).Select(r => r.LatencyMs).ToArray();
        if (latencies.Length == 0)
        {
            return new LatencySummary(0, null, null);
        }

        Array.Sort(latencies);
        return new LatencySummary(latencies.Length, NearestRank(latencies, 50), NearestRank(latencies, 95));
    }

    // The rank is ceil(q * n), computed in integers so that a q such as 0.95 cannot round the rank up.
    private static double NearestRank(double[] sorted, int percent)
    {
        var rank = Math.Max(1, ((percent * sorted.Length) + 99) / 100);
        return sorted[rank - 1];
    }

    private static CostSummary Cost(IReadOnlyList<ItemResult> items, PriceTable? prices)
    {
        int reported = 0, priced = 0, unknown = 0, cached = 0;
        var total = 0.0;
        var used = new List<ModelPrice>();
        foreach (var item in items)
        {
            if (item.Error is not null)
            {
                continue;
            }

            if (item.Cached)
            {
                cached++;
            }
            else if (item.Cost is { } cost && double.IsFinite(cost) && cost >= 0)
            {
                reported++;
                total += cost;
            }
            else if (item.Model is { } model
                && item.InputTokens is >= 0 and { } input
                && item.OutputTokens is >= 0 and { } output
                && prices is not null
                && prices.TryFind(model, out var price))
            {
                priced++;
                total += ((input * price.InputPerMillion) + (output * price.OutputPerMillion)) / TokensPerMillion;
                if (!used.Contains(price))
                {
                    used.Add(price);
                }
            }
            else
            {
                unknown++;
            }
        }

        var known = reported + priced;
        used.Sort(static (a, b) => string.CompareOrdinal(a.Model, b.Model));
        return new CostSummary(known == 0 ? null : total / known * 1000, reported, priced, unknown, cached, used);
    }

    private static ErrorSummary Errors(IReadOnlyList<ItemResult> items)
    {
        var byKind = new Dictionary<string, int>(StringComparer.Ordinal);
        var count = 0;
        foreach (var item in items)
        {
            if (item.Error is { } error)
            {
                count++;
                byKind[error.Kind] = byKind.GetValueOrDefault(error.Kind) + 1;
            }
        }

        var sorted = byKind.ToList();
        sorted.Sort(static (a, b) => string.CompareOrdinal(a.Key, b.Key));
        return new ErrorSummary(count, sorted);
    }

    private static List<ModelCount> Models(IReadOnlyList<ItemResult> items)
    {
        var byModel = new Dictionary<string, int>(StringComparer.Ordinal);
        var unnamed = 0;
        foreach (var item in items)
        {
            if (item.Error is not null)
            {
                continue;
            }

            if (item.Model is { } model)
            {
                byModel[model] = byModel.GetValueOrDefault(model) + 1;
            }
            else
            {
                unnamed++;
            }
        }

        var models = byModel.Select(p => new ModelCount(p.Key, p.Value)).ToList();
        if (unnamed > 0)
        {
            models.Add(new ModelCount(null, unnamed));
        }

        models.Sort(static (a, b) => a.Count != b.Count ? b.Count.CompareTo(a.Count) : string.CompareOrdinal(a.Model, b.Model));
        return models;
    }
}
