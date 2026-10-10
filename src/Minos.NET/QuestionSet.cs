using System.Text.Json;
using Minos.Protocols;

namespace Minos;

/// <summary>
/// A question set built at run time with <see cref="QuestionSetBuilder"/>: the counterpart of a <c>[Questions]</c>
/// type, for questions, options or keys known only at run time. Evaluate it with
/// <c>IDecisionClient.EvaluateAsync(QuestionSet, DecisionContent)</c>.
/// </summary>
/// <remarks>Immutable and safe to share across threads: build it once and reuse it.</remarks>
public sealed class QuestionSet
{
    private readonly AnswerParser<Answers> _parser;
    private readonly AnswerFactory<Answers> _create;

    internal QuestionSet(
        object identity, QuestionSetDefinition definition, QuestionFailure[] warnings, QuestionPlan[] plan)
    {
        Identity = identity;
        Definition = definition;
        _create = answers => new Answers(this, answers.Probabilities, answers.HeapSlots ?? answers.Slots.ToArray());
        _parser = (ref Utf8JsonReader answers) => SystemOneProtocol.Instance.ReadAnswers(ref answers, Definition, _create);
        Warnings = warnings.Length == 0 ? [] : new System.Collections.ObjectModel.ReadOnlyCollection<QuestionFailure>(warnings);
        Plan = plan;
    }

    /// <summary>Gets the <c>questions</c> object of a <c>/v1/systemone</c> request, as UTF-8 JSON, written on first use and cached.</summary>
    public ReadOnlySpan<byte> QuestionsUtf8 => SystemOneProtocol.QuestionsUtf8(Definition);

    /// <summary>Gets the set's questions, independent of any provider's wire format.</summary>
    public QuestionSetDefinition Definition { get; }

    /// <summary>Gets the advice the set's questions break: MIN003 and MIN005, which do not stop the build.</summary>
    public IReadOnlyList<QuestionFailure> Warnings { get; }

    /// <summary>Gets the identity token of the builder that created this set; every set that builder builds shares it.</summary>
    internal object Identity { get; }

    /// <summary>Gets how to read each question's answer, in wire order.</summary>
    internal QuestionPlan[] Plan { get; }

    /// <summary>Starts a new set.</summary>
    /// <returns>An empty builder.</returns>
    public static QuestionSetBuilder CreateBuilder() => new();

    /// <summary>Gets this set's parser, created once. It reads through the protocol; unknown keys are skipped, as a generated set skips them.</summary>
    internal AnswerParser<Answers> Parser => _parser;
}
