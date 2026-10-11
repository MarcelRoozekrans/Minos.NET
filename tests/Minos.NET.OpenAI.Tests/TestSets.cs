namespace Minos.OpenAI.Tests;

/// <summary>The question sets the OpenAI protocol tests share.</summary>
internal static class TestSets
{
    /// <summary>
    /// A Noul, a Choice with one described and one plain option, and a three-level Score: the guide's example request,
    /// and the set the response fixtures answer.
    /// </summary>
    public static QuestionSetDefinition AllKinds() => new(
        QuestionDefinition.Noul("refund", "Is this a refund request?"),
        QuestionDefinition.Choice(
            "topic",
            "Which topic is this about?",
            new OptionDefinition("billing", "Payments and invoices"),
            new OptionDefinition("other", null)),
        QuestionDefinition.Score("urgency", "How urgent is this?", "Not urgent", "Soon", "Right now"));

    /// <summary><see cref="AllKinds"/> and a Noul, <c>other_question</c>, which the refusal fixture refuses.</summary>
    public static QuestionSetDefinition WithOtherQuestion() => new([.. AllKinds().Questions, QuestionDefinition.Noul("other_question", "Is anything else asked?")]);
}
