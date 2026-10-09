using System.Text.Json;

namespace Minos;

/// <summary>
/// A question set built at run time with <see cref="JevQuestionSetBuilder"/>: the counterpart of a <c>[JevQuestions]</c>
/// type, for questions, options or keys known only at run time. Evaluate it with
/// <c>IJevClient.EvaluateAsync(JevQuestionSet, JevContent)</c>.
/// </summary>
/// <remarks>Immutable and safe to share across threads: build it once and reuse it.</remarks>
public sealed class JevQuestionSet
{
    private readonly byte[] _questionsUtf8;
    private readonly AnswerParser<JevAnswers> _parser;

    internal JevQuestionSet(
        object identity, byte[] questionsUtf8, JevQuestionFailure[] warnings, QuestionPlan[] plan, byte[][] utf8Keys, int probabilityCount)
    {
        Identity = identity;
        _parser = Parse;
        _questionsUtf8 = questionsUtf8;
        Warnings = warnings.Length == 0 ? [] : new System.Collections.ObjectModel.ReadOnlyCollection<JevQuestionFailure>(warnings);
        Plan = plan;
        QuestionKeys = utf8Keys;
        ProbabilityCount = probabilityCount;
    }

    /// <summary>Gets the <c>questions</c> object of a <c>/v1/systemone</c> request, as UTF-8 JSON, written once at build.</summary>
    public ReadOnlySpan<byte> QuestionsUtf8 => _questionsUtf8;

    /// <summary>Gets the advice the set's questions break: JEV003 and JEV005, which do not stop the build.</summary>
    public IReadOnlyList<JevQuestionFailure> Warnings { get; }

    /// <summary>Gets the identity token of the builder that created this set; every set that builder builds shares it.</summary>
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

    /// <summary>Gets this set's parser, created once.</summary>
    internal AnswerParser<JevAnswers> Parser => _parser;

    /// <summary>
    /// Reads the answers from the <c>answers</c> object. Keys are found by a linear, allocation-free scan; unknown keys
    /// are skipped, as a generated set skips them.
    /// </summary>
    /// <exception cref="JsonException">An answer is missing, has the wrong type, names an unknown option or level, or lacks a required field.</exception>
    internal JevAnswers Parse(ref Utf8JsonReader answers)
    {
        JevAnswerReader.EnsureStartObject(ref answers);
        var plan = Plan;
        var probabilities = new double[ProbabilityCount];
        var slots = new AnswerSlot[plan.Length];
        Span<bool> found = plan.Length <= 256 ? stackalloc bool[plan.Length] : new bool[plan.Length];

        while (JevAnswerReader.NextProperty(ref answers))
        {
            var index = Utf8Keys.IndexOf(ref answers, QuestionKeys);
            if (index < 0)
            {
                answers.Skip();
                continue;
            }

            answers.Read();
            var question = plan[index];
            switch (question.Kind)
            {
                case QuestionKind.Noul:
                    slots[index] = new AnswerSlot(0, JevAnswerReader.ReadNoul(ref answers).Probability, 0, 0);
                    break;
                case QuestionKind.Choice:
                    var (choice, confidence) = JevAnswerReader.ReadChoiceCore(ref answers, question.Options!, probabilities, question.Offset);
                    slots[index] = new AnswerSlot(choice, 0, confidence, question.Offset);
                    break;
                default:
                    var (level, expected, scoreConfidence) = JevAnswerReader.ReadScoreCore(
                        ref answers, question.Options!, probabilities, question.Offset);
                    slots[index] = new AnswerSlot(level, expected, scoreConfidence, question.Offset);
                    break;
            }

            found[index] = true;
        }

        for (var i = 0; i < plan.Length; i++)
        {
            if (!found[i])
            {
                throw JevAnswerReader.MissingAnswer(plan[i].Key);
            }
        }

        return new JevAnswers(this, probabilities, slots);
    }
}
