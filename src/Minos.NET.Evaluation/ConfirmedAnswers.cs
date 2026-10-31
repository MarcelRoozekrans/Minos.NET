using Minos.Recording;

namespace Minos.Evaluation;

// The confirmed answers of one decision, merged per key: an answer that does not fit its question is dropped first, so
// it never takes part in the merge, and then the latest At wins, a later outcome in the file winning an equal time. The
// dataset export and the shadow report both read confirmed outcomes through it, so they agree on what was confirmed.
internal sealed class ConfirmedAnswers
{
    private ConfirmedAnswers(
        List<ConfirmedOutcome> outcomes, Dictionary<string, (ConfirmedAnswer Answer, DateTimeOffset At, int Outcome)> winners, int dropped, int overridden)
    {
        Outcomes = outcomes;
        Winners = winners;
        Dropped = dropped;
        Overridden = overridden;
    }

    // The decision's confirmed outcomes, in file order.
    internal List<ConfirmedOutcome> Outcomes { get; }

    // Per key, the winning answer, its time, and the index in Outcomes of the outcome that supplied it.
    internal Dictionary<string, (ConfirmedAnswer Answer, DateTimeOffset At, int Outcome)> Winners { get; }

    // The answers that did not fit their question.
    internal int Dropped { get; }

    // The keys whose valid answers differ, so the merge chose one over another.
    internal int Overridden { get; }

    internal static ConfirmedAnswers Merge(RecordedDecision decision)
    {
        var confirmed = new List<ConfirmedOutcome>();
        foreach (var outcome in decision.Outcomes)
        {
            if (outcome is ConfirmedOutcome c)
            {
                confirmed.Add(c);
            }
        }

        // Outcomes come in file order, so ">=" lets a later one win an equal time.
        var definition = decision.Record.Definition;
        var winners = new Dictionary<string, (ConfirmedAnswer Answer, DateTimeOffset At, int Outcome)>(StringComparer.Ordinal);
        var valid = new Dictionary<string, List<ConfirmedAnswer>>(StringComparer.Ordinal);
        var dropped = 0;
        for (var i = 0; i < confirmed.Count; i++)
        {
            foreach (var (key, answer) in confirmed[i].Answers)
            {
                if (!Fits(definition, key, answer))
                {
                    dropped++;
                    continue;
                }

                if (!valid.TryGetValue(key, out var answers))
                {
                    valid[key] = answers = [];
                }

                answers.Add(answer);
                if (!winners.TryGetValue(key, out var winner) || confirmed[i].At >= winner.At)
                {
                    winners[key] = (answer, confirmed[i].At, i);
                }
            }
        }

        var overridden = 0;
        foreach (var (key, winner) in winners)
        {
            if (valid[key].Exists(a => a != winner.Answer))
            {
                overridden++;
            }
        }

        return new ConfirmedAnswers(confirmed, winners, dropped, overridden);
    }

    private static bool Fits(QuestionSetDefinition definition, string key, ConfirmedAnswer answer)
    {
        foreach (var question in definition.Questions)
        {
            if (!string.Equals(question.Key, key, StringComparison.Ordinal))
            {
                continue;
            }

            if (question.Kind != answer.Kind)
            {
                return false;
            }

            return answer.Kind switch
            {
                QuestionKind.Choice => HasOption(question, answer.ChoiceKey!),
                QuestionKind.Score => answer.ScoreLevel < question.Options.Count,
                _ => true,
            };
        }

        return false;
    }

    private static bool HasOption(QuestionDefinition question, string optionKey)
    {
        foreach (var option in question.Options)
        {
            if (string.Equals(option.Key, optionKey, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
