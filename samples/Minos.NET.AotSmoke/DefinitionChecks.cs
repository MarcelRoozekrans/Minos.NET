namespace Minos.AotSmoke;

/// <summary>
/// The provider-neutral question definitions under Native AOT: the three question factories, the option definition
/// and the set definition that precomputes keys and offsets.
/// </summary>
internal static class DefinitionChecks
{
    [Covers("static Minos.QuestionDefinition.Noul(string! key, Minos.DecisionContent instructions, Minos.DecisionContent? whenTrue = null, Minos.DecisionContent? whenFalse = null) -> Minos.QuestionDefinition!")]
    [Covers("static Minos.QuestionDefinition.Choice(string! key, Minos.DecisionContent instructions, params System.ReadOnlySpan<Minos.OptionDefinition!> options) -> Minos.QuestionDefinition!")]
    [Covers("static Minos.QuestionDefinition.Score(string! key, Minos.DecisionContent instructions, params System.ReadOnlySpan<Minos.Criterion!> levels) -> Minos.QuestionDefinition!")]
    [Covers("Minos.OptionDefinition.OptionDefinition(string! key, Minos.Criterion? criterion) -> void")]
    [Covers("Minos.QuestionSetDefinition.QuestionSetDefinition(params System.ReadOnlySpan<Minos.QuestionDefinition!> questions) -> void")]
    public static void QuestionSetDefinitionKeepsItsQuestions()
    {
        var definition = new QuestionSetDefinition(
            QuestionDefinition.Noul("is_urgent", "Is it urgent?", whenFalse: "It can wait"),
            QuestionDefinition.Choice("route_to", "Which team?", new OptionDefinition("billing", "Charges"), new OptionDefinition("other", null)),
            QuestionDefinition.Score("urgency", "How urgent?", "Can wait", "Today"));

        var noul = definition.Questions[0];
        var choice = definition.Questions[1];
        var score = definition.Questions[2];
        Program.Check(
            definition.Questions.Count == 3
                && noul.Kind == QuestionKind.Noul
                && noul.Options.Count == 0
                && choice.Kind == QuestionKind.Choice
                && string.Equals(choice.Options[1].Key, "other", StringComparison.Ordinal)
                && choice.Options[1].Criterion is null
                && score.Kind == QuestionKind.Score
                && string.Equals(score.Options[1].Key, "1", StringComparison.Ordinal),
            "a question set definition keeps its questions, option keys and kinds under Native AOT");
    }

    [Covers("Minos.AnswerSlots.AnswerSlots() -> void")]
    public static void EmptyAnswerSlotsHoldNoAnswers()
    {
        var empty = new AnswerSlots();

        Program.Check(empty.Count == 0, "the default AnswerSlots holds no answers under Native AOT");
    }

    // Smoke code cannot build an AnswerSlots with answers: its constructor is internal. The success path runs under
    // AOT through the generated Create methods and the typed evaluation checks. Here the index guard runs first, before
    // the definition or the option set is touched, so the option set is never used and a null one stands in.
    [Covers("Minos.AnswerSlots.Noul(int index) -> Minos.Noul")]
    [Covers("Minos.AnswerSlots.Choice<T>(int index, Minos.DecisionOptionSet<T>! options) -> Minos.Choice<T>")]
    [Covers("Minos.AnswerSlots.Score<T>(int index, Minos.DecisionOptionSet<T>! options) -> Minos.Score<T>")]
    public static void AnswerSlotsRefuseAnIndexOutOfRange()
    {
        Program.Check(
            ThrowsOutOfRange(static () => new AnswerSlots().Noul(0))
                && ThrowsOutOfRange(static () => new AnswerSlots().Choice(0, (DecisionOptionSet<Team>)null!))
                && ThrowsOutOfRange(static () => new AnswerSlots().Score(0, (DecisionOptionSet<Urgency>)null!)),
            "AnswerSlots refuses an index outside its answers under Native AOT");
    }

    private static bool ThrowsOutOfRange(Action read)
    {
        try
        {
            read();
            return false;
        }
        catch (ArgumentOutOfRangeException)
        {
            return true;
        }
    }
}
