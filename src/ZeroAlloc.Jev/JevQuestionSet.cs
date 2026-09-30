namespace ZeroAlloc.Jev;

/// <summary>
/// A question set built at run time with <see cref="JevQuestionSetBuilder"/>: the counterpart of a <c>[JevQuestions]</c>
/// type, for questions, options or keys known only at run time. Evaluate it with
/// <c>IJevClient.EvaluateAsync(JevQuestionSet, JevContent)</c>.
/// </summary>
/// <remarks>Immutable and safe to share across threads: build it once and reuse it.</remarks>
public sealed class JevQuestionSet
{
    private readonly byte[] _questionsUtf8;

    internal JevQuestionSet(
        object identity, byte[] questionsUtf8, JevQuestionFailure[] warnings, QuestionPlan[] plan, byte[][] utf8Keys, int probabilityCount)
    {
        Identity = identity;
        _questionsUtf8 = questionsUtf8;
        Warnings = warnings;
        Plan = plan;
        QuestionKeys = utf8Keys;
        ProbabilityCount = probabilityCount;
    }

    /// <summary>Gets the <c>questions</c> object of a <c>/v1/systemone</c> request, as UTF-8 JSON, written once at build.</summary>
    public ReadOnlySpan<byte> QuestionsUtf8 => _questionsUtf8;

    /// <summary>Gets the advice the set's questions break: JEV003 and JEV005, which do not stop the build.</summary>
    public IReadOnlyList<JevQuestionFailure> Warnings { get; }

    /// <summary>Gets the builder that created this set; its handles belong to this set.</summary>
    internal object Identity { get; }

    /// <summary>Gets how to read each question's answer, in wire order.</summary>
    internal QuestionPlan[] Plan { get; }

    /// <summary>Gets the question keys as UTF-8, in <see cref="Plan"/> order, for <see cref="Utf8Keys.IndexOf"/>.</summary>
    internal byte[][] QuestionKeys { get; }

    /// <summary>Gets the length of the probability buffer one parse needs.</summary>
    internal int ProbabilityCount { get; }

    /// <summary>Starts a new set.</summary>
    /// <returns>An empty builder.</returns>
    public static JevQuestionSetBuilder CreateBuilder() => new();
}
