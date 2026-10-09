using System.Text.Json.Serialization;

namespace Minos;

/// <summary>A typed answer to one question, returned under the question's id.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(NoulAnswer), "noul")]
[JsonDerivedType(typeof(ChoiceAnswer), "choice")]
[JsonDerivedType(typeof(ScoreAnswer), "score")]
public abstract class Answer
{
    private protected Answer()
    {
    }
}
