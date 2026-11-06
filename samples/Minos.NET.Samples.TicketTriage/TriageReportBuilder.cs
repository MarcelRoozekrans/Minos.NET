using Minos;
using Minos.Evaluation;

namespace Minos.Samples.TicketTriage;

/// <summary>Computes the triage report from the records and the dataset labels.</summary>
public static class TriageReportBuilder
{
    /// <summary>Builds the report.</summary>
    /// <param name="records">The records, one per item run.</param>
    /// <param name="dataset">The dataset the items came from, for the right answers.</param>
    /// <param name="prices">The price table, used from the cost section on.</param>
    /// <returns>The report.</returns>
    /// <exception cref="InvalidOperationException">A decided record names an item that is not in the dataset.</exception>
    public static TriageReport Build(IReadOnlyList<TriageRecord> records, DecisionDataset dataset, PriceTable prices)
    {
        ArgumentNullException.ThrowIfNull(records);
        ArgumentNullException.ThrowIfNull(dataset);
        ArgumentNullException.ThrowIfNull(prices);

        // The dataset reader rejects duplicate item ids, so each id appears once.
        var items = new Dictionary<string, DatasetItem>(StringComparer.Ordinal);
        foreach (var item in dataset)
        {
            items[item.Id] = item;
        }

        var failures = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var models = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var decided = 0;
        var tickets = new Dictionary<QuestionKind, int>();
        var allTickets = 0;
        var tally = new Tally();

        foreach (var record in records)
        {
            if (record.Error is { } error)
            {
                failures[error] = failures.GetValueOrDefault(error) + 1;
                continue;
            }

            if (!items.TryGetValue(record.Id, out var item))
            {
                throw new InvalidOperationException($"Record '{record.Id}' is not in the dataset.");
            }

            decided++;
            var targetCalled = false;
            foreach (var call in record.Calls)
            {
                if (call.Model is { } model)
                {
                    models[model] = models.GetValueOrDefault(model) + 1;
                }

                if (string.Equals(call.Client, TriagePipeline.TargetName, StringComparison.Ordinal)
                    && !string.Equals(call.Status, "NotCalledNoTimeLeft", StringComparison.Ordinal))
                {
                    targetCalled = true;
                }
            }

            allTickets += targetCalled ? 1 : 0;

            var ticketKinds = new HashSet<QuestionKind>();
            foreach (var question in record.Questions)
            {
                if (!item.Expected.TryGetValue(question.Key, out var expected))
                {
                    continue;
                }

                tally.Add(question, expected);
                if (question.Reason is "Escalated" or "TargetFailed")
                {
                    ticketKinds.Add(expected.Kind);
                }
            }

            foreach (var kind in ticketKinds)
            {
                tickets[kind] = tickets.GetValueOrDefault(kind) + 1;
            }
        }

        var run = new RunSection(records.Count, decided, failures, models);
        return new TriageReport(
            run,
            new PerKind<SplitCounts>(
                tally.Choice.Split(tickets.GetValueOrDefault(QuestionKind.Choice)),
                tally.Noul.Split(tickets.GetValueOrDefault(QuestionKind.Noul)),
                tally.All.Split(allTickets)),
            new PerKind<AccuracySection>(tally.Choice.Accuracy(), tally.Noul.Accuracy(), tally.All.Accuracy()),
            new PerKind<EscalatedTable>(tally.Choice.Table(), tally.Noul.Table(), tally.All.Table()),
            new PerKind<SignalSection>(tally.Choice.Signal(), tally.Noul.Signal(), tally.All.Signal()));
    }

    private sealed class Tally
    {
        public Bucket Choice { get; } = new();

        public Bucket Noul { get; } = new();

        public Bucket All { get; } = new();

        public void Add(TriageQuestion question, ExpectedAnswer expected)
        {
            var localRight = Judge.IsRight(question.Local, expected);
            // Only the target replaces the local answer; on any other reason the local answer stands, whatever Final holds.
            var finalRight = string.Equals(question.Reason, "Escalated", StringComparison.Ordinal)
                ? Judge.IsRight(question.Final, expected)
                : localRight;
            All.Add(question.Reason, localRight, finalRight);
            switch (expected.Kind)
            {
                case QuestionKind.Choice:
                    Choice.Add(question.Reason, localRight, finalRight);
                    break;
                case QuestionKind.Noul:
                    Noul.Add(question.Reason, localRight, finalRight);
                    break;
            }
        }
    }

    private sealed class Bucket
    {
        private int _confident;
        private int _escalated;
        private int _noTimeLeft;
        private int _targetFailed;
        private int _total;
        private int _localRight;
        private int _finalRight;
        private int _targetRight;
        private int _fixed;
        private int _broken;
        private int _bothRight;
        private int _bothWrong;
        private int _caught;
        private int _missed;
        private int _below;
        private int _belowLocalRight;

        public void Add(string reason, bool localRight, bool finalRight)
        {
            _total++;
            _localRight += localRight ? 1 : 0;
            _finalRight += finalRight ? 1 : 0;

            var below = !string.Equals(reason, "Confident", StringComparison.Ordinal);
            if (below)
            {
                _below++;
                _belowLocalRight += localRight ? 1 : 0;
            }

            if (!localRight)
            {
                if (below)
                {
                    _caught++;
                }
                else
                {
                    _missed++;
                }
            }

            switch (reason)
            {
                case "Confident":
                    _confident++;
                    break;
                case "Escalated":
                    _escalated++;
                    _targetRight += finalRight ? 1 : 0;
                    if (localRight && finalRight)
                    {
                        _bothRight++;
                    }
                    else if (localRight)
                    {
                        _broken++;
                    }
                    else if (finalRight)
                    {
                        _fixed++;
                    }
                    else
                    {
                        _bothWrong++;
                    }

                    break;
                case "NoTimeLeft":
                    _noTimeLeft++;
                    break;
                case "TargetFailed":
                    _targetFailed++;
                    break;
            }
        }

        public SplitCounts Split(int tickets) => new(_confident, _escalated, _noTimeLeft, _targetFailed, tickets);

        public AccuracySection Accuracy() => new(new Hits(_localRight, _total), new Hits(_finalRight, _total), new Hits(_targetRight, _escalated));

        public EscalatedTable Table() => new(_fixed, _broken, _bothRight, _bothWrong);

        public SignalSection Signal() => new(_caught, _missed, _below, _belowLocalRight);
    }
}
