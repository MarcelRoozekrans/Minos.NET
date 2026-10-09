using System.Text.Json.Serialization;

namespace Minos;

/// <summary>A typed question evaluated against the request <c>state</c>.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(NoulQuestion), "noul")]
[JsonDerivedType(typeof(ChoiceQuestion), "choice")]
[JsonDerivedType(typeof(ScoreQuestion), "score")]
public abstract class Question
{
    private protected Question()
    {
    }

    /// <summary>
    /// Gets what the model should evaluate: text, or structured JSON holding the question and data it refers to.
    /// </summary>
    public required DecisionContent Instructions { get; init; }
}
