using System.Text.Json.Serialization;

namespace Jev.Net;

/// <summary>A typed answer to one question, returned under the question's id.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(NoulAnswer), "noul")]
[JsonDerivedType(typeof(ChoiceAnswer), "choice")]
[JsonDerivedType(typeof(ScoreAnswer), "score")]
public abstract record JevAnswer
{
    private protected JevAnswer()
    {
    }
}
