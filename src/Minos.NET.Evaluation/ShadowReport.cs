using System.Diagnostics;
using Minos.Recording;

namespace Minos.Evaluation;

/// <summary>
/// The comparison of a primary provider and a shadow candidate, from recorded decisions whose records carry the
/// candidate's answers in <see cref="DecisionRecord.Shadow"/>: per question, how often the two agree and a confusion
/// table of their answers; per side, its calibration against the confirmed outcomes, its latency, cost and errors. It
/// holds no decision id, erasure key or state.
/// </summary>
public sealed class ShadowReport
{
    private ShadowReport(
        int decisionsRead,
        int decisionsShadowed,
        int calibrationPrimaryOnlyKeys,
        int calibrationCandidateOnlyKeys,
        ShadowSide primary,
        ShadowSide candidate,
        IReadOnlyList<ShadowQuestion> questions)
    {
        DecisionsRead = decisionsRead;
        DecisionsShadowed = decisionsShadowed;
        CalibrationPrimaryOnlyKeys = calibrationPrimaryOnlyKeys;
        CalibrationCandidateOnlyKeys = calibrationCandidateOnlyKeys;
        Primary = primary;
        Candidate = candidate;
        Questions = questions;
    }

    /// <summary>Gets the number of decisions read, shadowed or not.</summary>
    public int DecisionsRead { get; }

    /// <summary>Gets the number of decisions whose record has the candidate's part. Only these are compared.</summary>
    public int DecisionsShadowed { get; }

    /// <summary>
    /// Gets the number of questions of shadowed decisions with a merged confirmed answer that only the primary answered,
    /// so they are not calibrated on either side.
    /// </summary>
    public int CalibrationPrimaryOnlyKeys { get; }

    /// <summary>
    /// Gets the number of questions of shadowed decisions with a merged confirmed answer that only the candidate answered,
    /// so they are not calibrated on either side.
    /// </summary>
    public int CalibrationCandidateOnlyKeys { get; }

    /// <summary>Gets the primary's side, over the shadowed decisions.</summary>
    public ShadowSide Primary { get; }

    /// <summary>Gets the candidate's side, over the shadowed decisions.</summary>
    public ShadowSide Candidate { get; }

    /// <summary>Gets the questions of the shadowed decisions, in the order they first appear.</summary>
    public IReadOnlyList<ShadowQuestion> Questions { get; }

    /// <summary>
    /// Writes the report as JSON: <c>"kind":"minos-shadow-report","version":1</c>, every number at full precision, an
    /// unknown number as <c>null</c>, and each confusion table as a list of its non-zero cells.
    /// </summary>
    /// <returns>The JSON text.</returns>
    public string ToJson() => ShadowReportJsonWriter.Write(this);

    /// <summary>
    /// Writes the report as Markdown, with ratios rounded to four decimals and an unknown number as <c>n/a</c>. A confusion
    /// table of up to 12 labels is printed in full; a larger one as its 20 most frequent disagreements.
    /// </summary>
    /// <returns>The Markdown text.</returns>
    public string ToMarkdown() => ShadowReportMarkdownWriter.Write(this);

    /// <summary>
    /// Builds the report from decisions already read. Per shadowed decision:
    /// <list type="bullet">
    /// <item>Each question is compared when both sides answered it. An answer that does not fit its question, such as an
    /// option the question does not have or a probability outside 0 to 1, counts as not given.</item>
    /// <item>A Noul answer is yes when its probability is at least 0.5; Choice answers compare by option, Score answers by
    /// level.</item>
    /// <item>The confirmed outcomes merge per key as <see cref="DecisionDatasetExport"/> merges them: an answer that does not
    /// fit is dropped, then the latest <see cref="DecisionOutcome.At"/> wins. A question counts toward calibration, on both
    /// sides at once, when both sides answered it and a merged confirmed answer is left for it. Decisions with images count.</item>
    /// <item>The decision's signal outcomes are counted under each compared question: as agreed when the two sides gave
    /// that question the same answer, and as disagreed otherwise.</item>
    /// </list>
    /// A decision without the candidate's part is counted in <see cref="DecisionsRead"/> and is otherwise left out.
    /// </summary>
    /// <param name="decisions">The decisions, as <see cref="DecisionRecordFile.ReadAsync(string, CancellationToken)"/> returns them.</param>
    /// <param name="prices">The prices for calls whose provider reports no cost, or <see langword="null"/>.</param>
    /// <returns>The report.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="decisions"/> is <see langword="null"/>.</exception>
    public static ShadowReport Create(IEnumerable<RecordedDecision> decisions, PriceTable? prices = null)
    {
        ArgumentNullException.ThrowIfNull(decisions);
        var builder = new Builder();
        foreach (var decision in decisions)
        {
            builder.Add(decision);
        }

        return builder.Build(prices);
    }

    /// <summary>Builds the report as <see cref="Create(IEnumerable{RecordedDecision}, PriceTable?)"/> does, reading the decisions as they come.</summary>
    /// <param name="decisions">The decisions, as <see cref="DecisionRecordFile.ReadAsync(string, CancellationToken)"/> returns them.</param>
    /// <param name="prices">The prices for calls whose provider reports no cost, or <see langword="null"/>.</param>
    /// <param name="cancellationToken">Cancels the read between decisions.</param>
    /// <returns>The report.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="decisions"/> is <see langword="null"/>.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    public static Task<ShadowReport> CreateAsync(
        IAsyncEnumerable<RecordedDecision> decisions, PriceTable? prices = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(decisions);
        return CreateCoreAsync(decisions, prices, cancellationToken);
    }

    private static async Task<ShadowReport> CreateCoreAsync(
        IAsyncEnumerable<RecordedDecision> decisions, PriceTable? prices, CancellationToken cancellationToken)
    {
        var builder = new Builder();
        await foreach (var decision in decisions.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();
            builder.Add(decision);
        }

        return builder.Build(prices);
    }

    // The answers of one side that fit their question, by key, each with its option or level index.
    private static Dictionary<string, (AnswerRecord Answer, int Index)> Usable(QuestionSetDefinition definition, IReadOnlyList<RecordedAnswer> answers)
    {
        var byKey = new Dictionary<string, RecordedAnswer>(StringComparer.Ordinal);
        foreach (var answer in answers)
        {
            byKey[answer.Key] = answer;
        }

        var usable = new Dictionary<string, (AnswerRecord Answer, int Index)>(StringComparer.Ordinal);
        foreach (var question in definition.Questions)
        {
            if (!byKey.TryGetValue(question.Key, out var answer) || answer.Kind != question.Kind || !ResultsRules.IsDefined(answer.ProbabilitySource))
            {
                continue;
            }

            if (question.Kind == QuestionKind.Noul)
            {
                if (ResultsRules.IsUnit(answer.Probability))
                {
                    usable[question.Key] = (AnswerRecord.ForNoul(answer.Probability, answer.ProbabilitySource), -1);
                }

                continue;
            }

            if (answer.ChosenIndex < 0 || answer.ChosenIndex >= question.Options.Count || !ResultsRules.IsUnit(answer.Confidence))
            {
                continue;
            }

            usable[question.Key] = question.Kind == QuestionKind.Choice
                ? (AnswerRecord.ForChoice(question.Options[answer.ChosenIndex].Key, answer.Confidence, answer.ProbabilitySource), answer.ChosenIndex)
                : (AnswerRecord.ForScore(answer.ChosenIndex, answer.Confidence, answer.ProbabilitySource), answer.ChosenIndex);
        }

        return usable;
    }

    private static ExpectedAnswer Expected(ConfirmedAnswer answer) => answer.Kind switch
    {
        QuestionKind.Noul => ExpectedAnswer.ForNoul(answer.Noul),
        QuestionKind.Choice => ExpectedAnswer.ForChoice(answer.ChoiceKey!),
        QuestionKind.Score => ExpectedAnswer.ForScore(answer.ScoreLevel),
        _ => throw new UnreachableException(),
    };

    // One side's call as an item result, so latency, cost, errors and models are computed as the calibration report
    // computes them. It holds no id: the report never needs one.
    private static ItemResult Call(TimeSpan latency, bool cached, string? model, DecisionUsage? usage, DecisionErrorKind? errorKind)
        => new()
        {
            Id = string.Empty,
            Line = 0,
            LatencyMs = latency.TotalMilliseconds,
            Cached = cached,
            Model = model,
            InputTokens = usage?.InputTokens,
            OutputTokens = usage?.OutputTokens,
            Cost = usage?.Cost,
            Error = errorKind is { } kind ? new ItemError { Kind = kind.ToString(), Message = string.Empty } : null,
        };

    // Collects both sides and the question groups, one decision at a time.
    private sealed class Builder
    {
        private readonly Side _primary = new();
        private readonly Side _candidate = new();
        private readonly List<Group> _groups = [];
        private readonly Dictionary<(string Key, QuestionKind Kind), List<Group>> _byKey = [];
        private int _read;
        private int _shadowed;
        private int _calibratedDecisions;
        private int _calibrationPrimaryOnly;
        private int _calibrationCandidateOnly;

        public void Add(RecordedDecision decision)
        {
            _read++;
            var record = decision.Record;
            if (record.Shadow is not { } shadow)
            {
                return;
            }

            _shadowed++;
            _primary.Calls.Add(Call(record.Latency, record.IsCached, record.ServedModel, record.Usage, record.ErrorKind));
            _candidate.Calls.Add(Call(shadow.Latency, false, shadow.ServedModel, shadow.Usage, shadow.ErrorKind));
            _primary.Refusals += record.RefusedKeys.Count;
            _candidate.Refusals += shadow.RefusedKeys.Count;
            var primary = Usable(record.Definition, record.Answers);
            var candidate = Usable(record.Definition, shadow.Answers);

            var confirmed = ConfirmedAnswers.Merge(decision).Winners;
            var calibrated = false;
            foreach (var question in record.Definition.Questions)
            {
                var group = GroupOf(question);
                var byPrimary = primary.TryGetValue(question.Key, out var p);
                var byCandidate = candidate.TryGetValue(question.Key, out var c);
                var isConfirmed = confirmed.TryGetValue(question.Key, out var winner);
                if (!byPrimary || !byCandidate)
                {
                    group.PrimaryOnly += byPrimary ? 1 : 0;
                    group.CandidateOnly += byCandidate ? 1 : 0;

                    // A confirmed answer that only one side can be measured on: counted, so the report says what it left out.
                    _calibrationPrimaryOnly += isConfirmed && byPrimary ? 1 : 0;
                    _calibrationCandidateOnly += isConfirmed && byCandidate ? 1 : 0;
                    continue;
                }

                group.AddSignals(decision.Outcomes, group.Compare(p.Answer, p.Index, c.Answer, c.Index));
                if (isConfirmed)
                {
                    // The same item, at the same position, on both sides.
                    var expected = Expected(winner.Answer);
                    var order = _primary.Scored.Count;
                    _primary.Scored.Add(MetricsCalculator.Scored(question.Key, expected, p.Answer, order));
                    _candidate.Scored.Add(MetricsCalculator.Scored(question.Key, expected, c.Answer, order));
                    _primary.Verbalized += p.Answer.ProbabilitySource == ProbabilitySource.Verbalized ? 1 : 0;
                    _candidate.Verbalized += c.Answer.ProbabilitySource == ProbabilitySource.Verbalized ? 1 : 0;
                    calibrated = true;
                }
            }

            _calibratedDecisions += calibrated ? 1 : 0;
        }

        public ShadowReport Build(PriceTable? prices)
            => new(
                _read,
                _shadowed,
                _calibrationPrimaryOnly,
                _calibrationCandidateOnly,
                _primary.Build(prices, _calibratedDecisions), _candidate.Build(prices, _calibratedDecisions), [.. _groups.Select(g => g.Build())]);

        // A group is its key, kind and labels; the labels are compared only among groups of the same key and kind.
        private Group GroupOf(QuestionDefinition question)
        {
            if (!_byKey.TryGetValue((question.Key, question.Kind), out var sameKey))
            {
                _byKey[(question.Key, question.Kind)] = sameKey = [];
            }

            foreach (var group in sameKey)
            {
                if (group.HasLabels(question))
                {
                    return group;
                }
            }

            var added = new Group(question);
            sameKey.Add(added);
            _groups.Add(added);
            return added;
        }
    }

    private sealed class Side
    {
        public List<ItemResult> Calls { get; } = [];

        public List<MetricsCalculator.ScoredQuestion> Scored { get; } = [];

        public int Refusals { get; set; }

        public int Verbalized { get; set; }

        public ShadowSide Build(PriceTable? prices, int calibratedDecisions)
            => new(
                MetricsCalculator.Models(Calls),
                MetricsCalculator.Errors(Calls),
                Refusals,
                MetricsCalculator.Latency(Calls),
                MetricsCalculator.Cost(Calls, prices),
                Verbalized,
                MetricsCalculator.ForKinds(Scored),
                calibratedDecisions,
                Scored.Count);
    }

    private sealed class Group
    {
        private readonly string _key;
        private readonly QuestionKind _kind;
        private readonly string[] _labels;

        // The confusion table, primary row by candidate column, for Choice and Score; empty for Noul.
        private readonly int[] _cells;
        private readonly Dictionary<string, int> _agreedSignals = new(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _disagreedSignals = new(StringComparer.Ordinal);
        private int _compared;
        private int _agreed;
        private long _levelDifference;

        public Group(QuestionDefinition question)
        {
            _key = question.Key;
            _kind = question.Kind;
            _labels = question.Kind == QuestionKind.Noul ? [] : [.. question.Options.Select(o => o.Key)];
            _cells = new int[_labels.Length * _labels.Length];
        }

        public int PrimaryOnly { get; set; }

        public int CandidateOnly { get; set; }

        public bool HasLabels(QuestionDefinition question)
        {
            if (question.Kind == QuestionKind.Noul)
            {
                return true;
            }

            if (question.Options.Count != _labels.Length)
            {
                return false;
            }

            for (var i = 0; i < _labels.Length; i++)
            {
                if (!string.Equals(question.Options[i].Key, _labels[i], StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }

        // Counts one comparison and returns whether the two answers are the same.
        public bool Compare(AnswerRecord primary, int primaryIndex, AnswerRecord candidate, int candidateIndex)
        {
            _compared++;
            bool same;
            if (_kind == QuestionKind.Noul)
            {
                same = primary.Noul == candidate.Noul;
            }
            else
            {
                _cells[(primaryIndex * _labels.Length) + candidateIndex]++;
                _levelDifference += _kind == QuestionKind.Score ? Math.Abs(primaryIndex - candidateIndex) : 0;
                same = primaryIndex == candidateIndex;
            }

            _agreed += same ? 1 : 0;
            return same;
        }

        public void AddSignals(IReadOnlyList<DecisionOutcome> outcomes, bool agreed)
        {
            var counts = agreed ? _agreedSignals : _disagreedSignals;
            foreach (var outcome in outcomes)
            {
                if (outcome is SignalOutcome signal)
                {
                    counts[signal.Name] = counts.GetValueOrDefault(signal.Name) + 1;
                }
            }
        }

        public ShadowQuestion Build()
        {
            var confusion = new List<ConfusionCell>();
            for (var i = 0; i < _cells.Length; i++)
            {
                if (_cells[i] > 0)
                {
                    confusion.Add(new ConfusionCell(_labels[i / _labels.Length], _labels[i % _labels.Length], _cells[i]));
                }
            }

            return new ShadowQuestion(
                _key,
                _kind,
                _labels,
                _compared,
                PrimaryOnly,
                CandidateOnly,
                _compared == 0 ? null : (double)_agreed / _compared,
                _kind == QuestionKind.Score && _compared > 0 ? (double)_levelDifference / _compared : null,
                confusion,
                Sorted(_agreedSignals),
                Sorted(_disagreedSignals));
        }

        private static KeyValuePair<string, int>[] Sorted(Dictionary<string, int> counts)
        {
            var sorted = counts.ToArray();
            Array.Sort(sorted, static (a, b) => string.CompareOrdinal(a.Key, b.Key));
            return sorted;
        }
    }
}
