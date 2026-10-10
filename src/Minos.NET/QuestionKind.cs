namespace Minos;

/// <summary>The kind of a question in a <see cref="QuestionDefinition"/>.</summary>
public enum QuestionKind
{
    /// <summary>A yes-or-no question, answered with a probability.</summary>
    Noul,

    /// <summary>A question with one answer from a fixed set of options.</summary>
    Choice,

    /// <summary>A question rated on ordered levels.</summary>
    Score,
}
