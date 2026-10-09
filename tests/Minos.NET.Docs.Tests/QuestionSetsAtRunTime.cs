namespace Minos.Docs.Tests;

#region QuestionSets_Build
using Minos;

// The levels of a Score are given to the builder, so the enum needs no attributes.
public enum Priority
{
    Low,
    Medium,
    High,
}

public sealed class TenantRouter
{
    private readonly QuestionSet _questions;
    private readonly NoulHandle _urgent;
    private readonly KeyedChoiceHandle _team;
    private readonly ScoreHandle<Priority> _priority;

    // The teams come from a tenant's own data, so no enum can list them. Build the set once, here, and keep it.
    public TenantRouter(IEnumerable<(string Key, string Summary)> teams)
    {
        var built = QuestionSet.CreateBuilder()
            .Noul("urgent", "Does this message convey urgency?", out _urgent, criteria => criteria
                .WhenTrue("The sender needs an answer today")
                .WhenFalse("The sender can wait"))
            .Choice("team", "Which team should handle this?", out _team, options =>
            {
                foreach (var (key, summary) in teams)
                {
                    options.Option(key, summary);
                }
            })
            .Score("priority", "How soon should this be handled?", out _priority, levels => levels
                .Level(Priority.Low, "Can wait")
                .Level(Priority.Medium, "This week")
                .Level(Priority.High, "Today"))
            .Build();

        if (built.IsFailure)
        {
            // Failures lists every rule the set breaks, so a bad tenant configuration is reported in full.
            var problems = new List<string>();
            foreach (var failure in built.Error.Failures)
            {
                problems.Add($"{failure.Rule} on '{failure.QuestionKey}': {failure.Message}");
            }

            throw new InvalidOperationException(string.Join("; ", problems));
        }

        _questions = built.Value;
    }

    public async Task<(bool Urgent, string Team, Priority Priority)?> RouteAsync(
        IDecisionClient jev, string message, CancellationToken cancellationToken)
    {
        // The state is a DecisionContent. Text converts to one, so a string can be passed as it is.
        var result = await jev.EvaluateAsync(_questions, message, cancellationToken);
        if (result.IsFailure)
        {
            return null;
        }

        // Each handle gives back the answer type of its question: a Noul, a KeyedChoice and a Score of Priority.
        var answers = result.Value;
        return (answers.Get(_urgent).Value, answers.Get(_team).Value, answers.Get(_priority).Value);
    }
}
#endregion

public enum ServiceTeam
{
    Billing,
    Technical,
    Sales,
}

public static class CriteriaExample
{
    #region QuestionSets_Criteria
    public static (QuestionSet Set, ChoiceHandle<ServiceTeam> Team) Create()
    {
        var built = QuestionSet.CreateBuilder()
            .Choice("team", "Which team should handle this?", out ChoiceHandle<ServiceTeam> team, options => options
                // Text, with examples of what belongs and what does not.
                .Describe(
                    ServiceTeam.Billing,
                    Criterion.Text("Payments, invoices and refunds")
                        .WithExamples("I was charged twice")
                        .WithNotFor("How much is the Pro plan?"))
                // A JSON object or array can be the description as well.
                .Describe(
                    ServiceTeam.Technical,
                    Criterion.Json(DecisionContent.FromUtf8Json("""{"scope":"bugs","also":["outages","integrations"]}"""u8)))
                // A string converts to a plain text criterion. A member left undescribed is sent with no description.
                .Describe(ServiceTeam.Sales, "Pricing and upgrades"))
            .Build();

        return (built.IsSuccess ? built.Value : throw new InvalidOperationException(built.Error.Message), team);
    }
    #endregion
}

public static class RuleChecks
{
    #region QuestionSets_Failures
    // A set that breaks a rule does not build. Build returns a DecisionErrorKind.InvalidQuestions error, and its
    // Failures list every rule that was broken, each with the key of the question at fault.
    public static IReadOnlyList<string> BrokenRules()
    {
        var built = QuestionSet.CreateBuilder()
            .Choice("team", "Which team should handle this?", out KeyedChoiceHandle _, options => { })  // no options
            .Noul("team", "Is this urgent?", out NoulHandle _)                                          // a key used twice
            .Build();

        var broken = new List<string>();
        if (built.IsFailure)
        {
            foreach (var failure in built.Error.Failures)
            {
                broken.Add($"{failure.Rule} {failure.QuestionKey}");
            }
        }

        return broken;
    }
    #endregion

    #region QuestionSets_Warnings
    // A warning does not stop the build. The set is returned, with the advice on its Warnings.
    public static IReadOnlyList<string> Advice()
    {
        var built = QuestionSet.CreateBuilder()
            .Noul("urgent", " ", out NoulHandle _)                                       // blank instructions
            .Score("mood", "How does the customer feel?", out KeyedScoreHandle _, levels => levels
                .Level("Neutral"))                                                       // a Score with one level
            .Build();

        var advice = new List<string>();
        if (built.IsSuccess)
        {
            foreach (var warning in built.Value.Warnings)
            {
                advice.Add($"{warning.Rule} {warning.QuestionKey}");
            }
        }

        return advice;
    }
    #endregion
}
