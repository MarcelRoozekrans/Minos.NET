namespace ZeroAlloc.Jev;

/// <summary>
/// Marks a partial class or record whose <see cref="NoulAttribute"/>, <see cref="ChoiceAttribute"/> and
/// <see cref="ScoreAttribute"/> properties the ZeroAlloc.Jev source generator turns into a <c>/v1/systemone</c>
/// <c>questions</c> object and a typed answer parser.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class JevQuestionsAttribute : Attribute;
