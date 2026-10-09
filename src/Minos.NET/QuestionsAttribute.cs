namespace Minos;

/// <summary>
/// Marks a partial class or record whose <see cref="NoulAttribute"/>, <see cref="ChoiceAttribute"/> and
/// <see cref="ScoreAttribute"/> properties the Minos.NET source generator turns into a <c>/v1/systemone</c>
/// <c>questions</c> object and a typed answer parser. When <see cref="State"/> is set, the generated type also
/// implements <see cref="IQuestionSet{TSelf, TState}"/>, linking the set to the state type accepted by
/// <c>EvaluateAsync&lt;T, TState&gt;</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class QuestionsAttribute : Attribute
{
    /// <summary>
    /// Gets or sets the state type this question set evaluates against, or <see langword="null"/> for a stateless set.
    /// Must be a class, struct, record or array type: not an interface, an open generic type definition,
    /// <see langword="void"/>, a delegate or an enum.
    /// </summary>
    public Type? State { get; set; }
}
