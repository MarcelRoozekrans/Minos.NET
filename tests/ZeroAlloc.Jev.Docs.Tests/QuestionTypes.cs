namespace ZeroAlloc.Jev.Docs.Tests;

#region QuestionTypes_Questions
public enum Department
{
    [Criteria("Payments, invoices and refunds")]
    Billing,

    [Criteria("Bugs, outages and integrations")]
    Technical,

    [Criteria("Pricing, upgrades and new accounts")]
    Sales,
}

// The members of a Score's enum are its levels, lowest first: the first member is level 0.
public enum Mood
{
    [Level("Annoyed or angry")]
    Annoyed,

    [Level("Neutral")]
    Neutral,

    [Level("Pleased or grateful")]
    Pleased,
}

[JevQuestions]
public partial record TicketAnalysis
{
    [Noul("Does this convey urgency?")]
    public partial Noul IsUrgent { get; }

    [Choice("Which department should handle this?")]
    public partial Choice<Department> Department { get; }

    [Score("How does the customer feel?")]
    public partial Score<Mood> Mood { get; }
}
#endregion

public static class AnswerReading
{
    #region QuestionTypes_ReadNoul
    public static (bool Yes, double Probability) ReadNoul(Noul urgent)
    {
        // Probability is the chance, from 0 to 1, that the answer is yes. A Noul has no confidence:
        // the probability is the whole answer. Value is a convenience that is true from 0.5 up.
        return (urgent.Value, urgent.Probability);
    }
    #endregion

    #region QuestionTypes_ReadChoice
    public static (Department Picked, double Confidence, double PickedProbability, double SalesProbability) ReadChoice(
        Choice<Department> department)
    {
        // Value is the option Jev picked, and Confidence says how far to trust that pick.
        // Probabilities holds every option's probability, looked up by the enum member.
        return (
            department.Value,
            department.Confidence,
            department.Probabilities[department.Value],
            department.Probabilities[Department.Sales]);
    }

    // The map can be enumerated too, in the order of the enum's members, without allocating.
    public static Department? RunnerUp(Choice<Department> department)
    {
        Department? runnerUp = null;
        var best = -1.0;
        foreach (var (option, probability) in department.Probabilities)
        {
            if (option != department.Value && probability > best)
            {
                runnerUp = option;
                best = probability;
            }
        }

        return runnerUp;
    }
    #endregion

    #region QuestionTypes_ReadScore
    public static (Mood Level, double Expected, double Normalized, double Confidence) ReadScore(Score<Mood> mood)
    {
        // Value is the most probable level. Expected is the probability-weighted average level index, so it
        // can fall between levels: 0.5 would sit halfway between level 0 and level 1. Normalized rescales it
        // to 0 to 1, so Scores with different numbers of levels can be compared and weighted.
        return (mood.Value, mood.Expected, mood.Normalized, mood.Confidence);
    }
    #endregion
}

#region QuestionTypes_Keyed
public sealed class PlanAdvisor
{
    private readonly JevQuestionSet _questions;
    private readonly KeyedChoiceHandle _plan;
    private readonly KeyedScoreHandle _effort;

    // The options and levels come from run-time data, so no enum describes them: keys and levels are plain values.
    public PlanAdvisor(IEnumerable<(string Key, string Summary)> plans)
    {
        var built = JevQuestionSet.CreateBuilder()
            .Choice("plan", "Which plan fits this customer?", out _plan, options =>
            {
                foreach (var (key, summary) in plans)
                {
                    options.Option(key, summary);
                }
            })
            .Score("effort", "How much setup work does the customer need?", out _effort, levels => levels
                .Level("Minutes")
                .Level("Hours")
                .Level("Days"))
            .Build();

        _questions = built.IsSuccess ? built.Value : throw new InvalidOperationException(built.Error.Message);
    }

    public async Task<(string Plan, double PlanProbability, int EffortLevel, double EffortNormalized)?> AdviseAsync(
        IJevClient jev, string message, CancellationToken ct)
    {
        var result = await jev.EvaluateAsync(_questions, message, ct);
        if (result.IsFailure)
        {
            return null;
        }

        var plan = result.Value.Get(_plan);       // KeyedChoice: Value is the option's key, a string
        var effort = result.Value.Get(_effort);   // KeyedScore: Level is the level's index, an int

        // Probabilities are looked up by key. For a keyed Score the keys are the level indexes, "0", "1" and so on,
        // and an int index reads the same entry by position.
        return (plan.Value, plan.Probabilities[plan.Value], effort.Level, effort.Normalized);
    }
}
#endregion
