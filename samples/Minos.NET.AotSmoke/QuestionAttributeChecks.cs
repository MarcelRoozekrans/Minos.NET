namespace Minos.AotSmoke;

/// <summary>
/// The question-set attributes' constructors under Native AOT: <see cref="JevQuestionsAttribute"/>,
/// <see cref="NoulAttribute"/>, <see cref="ChoiceAttribute"/>, <see cref="ScoreAttribute"/>,
/// <see cref="CriteriaAttribute"/> and <see cref="LevelAttribute"/>. The generator reads them at compile time, so
/// nothing constructs them at run time unless a caller does, as reflection-based tooling would.
/// </summary>
internal static class QuestionAttributeChecks
{
    [Covers("Minos.JevQuestionsAttribute.JevQuestionsAttribute() -> void")]
    [Covers("Minos.NoulAttribute.NoulAttribute(string! instructions) -> void")]
    [Covers("Minos.ChoiceAttribute.ChoiceAttribute(string! instructions) -> void")]
    [Covers("Minos.ScoreAttribute.ScoreAttribute(string! instructions) -> void")]
    [Covers("Minos.CriteriaAttribute.CriteriaAttribute(string! description) -> void")]
    [Covers("Minos.LevelAttribute.LevelAttribute(string! description) -> void")]
    public static void AttributesKeepWhatTheyAreGiven()
    {
        var questions = new JevQuestionsAttribute { State = typeof(SmokeState) };
        var noul = new NoulAttribute("Does `message` ask for a credential?") { WhenTrue = "It asks for a password", Key = "credentials" };
        var choice = new ChoiceAttribute("Which team should handle `message`?") { Key = "team" };
        var score = new ScoreAttribute("How urgent is `message`?");
        var criteria = new CriteriaAttribute("Charges, invoices, refunds") { Examples = ["I was charged twice"], NotFor = ["How much is Pro?"] };
        var level = new LevelAttribute("Today") { Examples = ["The site is down"] };

        Program.Check(
            questions.State == typeof(SmokeState)
                && string.Equals(noul.Instructions, "Does `message` ask for a credential?", StringComparison.Ordinal)
                && string.Equals(noul.WhenTrue, "It asks for a password", StringComparison.Ordinal)
                && string.Equals(choice.Key, "team", StringComparison.Ordinal)
                && string.Equals(score.Instructions, "How urgent is `message`?", StringComparison.Ordinal)
                && score.Key is null
                && string.Equals(criteria.Description, "Charges, invoices, refunds", StringComparison.Ordinal)
                && criteria.NotFor is ["How much is Pro?"]
                && string.Equals(level.Description, "Today", StringComparison.Ordinal)
                && level.Examples is ["The site is down"],
            "the question-set attributes keep their instructions, descriptions and named values under Native AOT");
    }
}
