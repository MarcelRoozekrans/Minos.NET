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

    // One scored question. Order is its position in item order, then the item's question order. Probability is the
    // saved p of an answered Noul question, and null otherwise.
    private readonly record struct ScoredQuestion(
        QuestionKind Kind, string Key, ExpectedAnswer Expected, bool Answered, bool Correct, double Confidence, double? Probability, int Order);

    // The results-file rules first, then the fit to the dataset. Each problem's line is the dataset line of the item it
    // is about; a header problem has line 0, and an item past the dataset's end the line after its last item.
    internal static DatasetProblems? Match(EvaluationResults results, DecisionDataset dataset)
    {
        var problems = new List<DatasetProblem>();
        foreach (var problem in ResultsRules.Check(results))
        {
            problems.Add(new DatasetProblem(problem.Index is { } index ? LineOf(dataset, index) : 0, problem.Message));
        }

        var count = results.Items.Count;
        if (count != dataset.Count)
        {
            problems.Add(new DatasetProblem(
                LineOf(dataset, Math.Min(count, dataset.Count)),
                string.Create(CultureInfo.InvariantCulture, $"The results have {count} items, but the dataset has {dataset.Count}.")));
        }

        var shared = Math.Min(count, dataset.Count);
        for (var i = 0; i < shared; i++)
        {
            if (results.Items[i] is not { } result)
            {
                continue;
            }

            var expected = dataset[i];
            if (!string.Equals(result.Id, expected.Id, StringComparison.Ordinal))
            {
                problems.Add(new DatasetProblem(
                    expected.Line,
                    string.Create(CultureInfo.InvariantCulture, $"Item {i + 1} of the results is '{result.Id}', but the dataset's is '{expected.Id}'.")));
            }

            CheckAnswers(i + 1, result, expected, problems);
        }

        return problems.Count == 0 ? null : new DatasetProblems([.. problems]);
    }

    private static int LineOf(DecisionDataset dataset, int index)
        => index < dataset.Count ? dataset[index].Line : dataset.Count == 0 ? 1 : dataset[dataset.Count - 1].Line + 1;

    // Every saved answer must fit the item's own question. A missing answer is not a problem: the runner leaves out
    // the answers a provider omitted, and those questions count as scored but unanswered.
    private static void CheckAnswers(int position, ItemResult result, DatasetItem item, List<DatasetProblem> problems)
    {
        if (result.Error is not null || result.Answers is null)
        {
            return;
        }

        var keys = result.Answers.Keys.ToList();
        keys.Sort(StringComparer.Ordinal);
        foreach (var key in keys)
        {
            if (result.Answers[key] is not { } answer)
            {
                continue;
            }

            var problem = Check(item, key, answer);
            if (problem is not null)
            {
                problems.Add(new DatasetProblem(item.Line, string.Create(CultureInfo.InvariantCulture, $"Item {position} '{item.Id}': {problem}")));
            }
        }
    }

    // An AnswerRecord's own values are valid by construction; what is left is whether it fits the question.
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

        return question.Kind switch
        {
            QuestionKind.Choice when !HasOption(question, answer.ChoiceKey)
                => $"the answer '{key}' chose '{answer.ChoiceKey}', which is not an option of the question.",
            QuestionKind.Score when answer.ScoreLevel >= question.Options.Count
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
                    answered ? answer!.Probability : null,
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

    // The bin is taken from the confidence. For a Noul answer that is 1 - p when p < 0.5, and for every p of two
    // decimals 1 - p lands in the bin of its decimal value, 0.3 giving 0.7 in bin 7, so the bins need no mirrored rule.
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
            var mirrored = (SweepPoints - i) / 100.0;
            var covered = 0;
            var correct = 0;
            foreach (var question in answered)
            {
                if (IsCovered(question, threshold, mirrored))
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

    // A Noul answer's confidence max(p, 1 - p) is compared through p itself: p >= t, or p <= 1 - t with 1 - t computed
    // as (100 - i) / 100.0. Computing 1 - p instead would put p = 0.07 one step below 0.93, uncovered at its own threshold.
    private static bool IsCovered(ScoredQuestion question, double threshold, double mirrored)
        => question.Probability is { } p ? p >= threshold || p <= mirrored : question.Confidence >= threshold;

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

    // A response came back and was billed: the request succeeded, or its answers did not fit the item. An
    // InvalidResponse item keeps the response's model, tokens and cost, and counts for cost and models; it stays out of
    // latency and accuracy, which describe answers that could be used.
    private static bool WasServed(ItemResult item)
        => item.Error is not { } error || string.Equals(error.Kind, nameof(DecisionErrorKind.InvalidResponse), StringComparison.Ordinal);

    private static CostSummary Cost(IReadOnlyList<ItemResult> items, PriceTable? prices)
    {
        int reported = 0, priced = 0, unknown = 0, cached = 0;
        var total = 0.0;
        var used = new List<ModelPrice>();
        foreach (var item in items)
        {
            if (!WasServed(item))
            {
                continue;
            }

            if (item.Cached)
            {
                cached++;
            }
            else if (item.Cost is { } cost)
            {
                reported++;
                total += cost;
            }
            else if (item.Model is { } model
                && item.InputTokens is { } input
                && item.OutputTokens is { } output
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
            if (!WasServed(item))
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
