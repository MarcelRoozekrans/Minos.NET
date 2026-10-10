namespace Minos;

/// <summary>
/// The number of questions in a <c>[Questions]</c> set, for the client's logs and its
/// <c>minos.request.question_count</c> span tag: the count of its <see cref="IQuestionSet{TSelf}.Definition"/>.
/// </summary>
/// <typeparam name="T">The question set.</typeparam>
internal static class GeneratedQuestionCount<T>
    where T : IQuestionSet<T>
{
    /// <summary>The set's question count.</summary>
    public static readonly int Value = T.Definition.Questions.Count;
}
