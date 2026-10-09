using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Minos.Serialization;
using Minos.Validation;
using ZeroAlloc.Results;

namespace Minos;

/// <summary>
/// Builds a <see cref="QuestionSet"/> at run time. Each question method takes the question's wire key, its
/// instructions and an <c>out</c> handle to read its answer with, and returns this builder.
/// </summary>
/// <remarks>
/// Not thread-safe. <see cref="Build"/> checks the questions against the API's rules and can be called again after
/// adding more; every set built by one builder accepts that builder's handles. A call that throws adds no question.
/// A configurator passed to <c>configure</c> works only inside that callback: once it returns, the configurator throws
/// <see cref="InvalidOperationException"/>.
/// </remarks>
public sealed class QuestionSetBuilder
{
    private readonly object _identity = new();
    private readonly List<QuestionDraft> _questions = [];

    internal QuestionSetBuilder()
    {
    }

    /// <summary>Adds a Noul question: a yes/no question answered with a probability.</summary>
    /// <param name="key">The question's wire key.</param>
    /// <param name="instructions">The question: text, or JSON.</param>
    /// <param name="question">The handle to read the answer with.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="key"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="instructions"/> is uninitialized.</exception>
    public QuestionSetBuilder Noul(string key, DecisionContent instructions, out NoulHandle question)
    {
        var draft = Draft(key, QuestionKind.Noul, instructions, enumOptions: null, enumMembers: null);
        question = new NoulHandle(_identity, Add(draft));
        return this;
    }

    /// <summary>Adds a Noul question with descriptions of what a yes and a no mean.</summary>
    /// <param name="key">The question's wire key.</param>
    /// <param name="instructions">The question: text, or JSON.</param>
    /// <param name="question">The handle to read the answer with.</param>
    /// <param name="configure">Sets the descriptions.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="key"/> or <paramref name="configure"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="instructions"/> is uninitialized.</exception>
    public QuestionSetBuilder Noul(string key, DecisionContent instructions, out NoulHandle question, Action<NoulCriteriaBuilder> configure)
    {
        var draft = Draft(key, QuestionKind.Noul, instructions, enumOptions: null, enumMembers: null);
        ArgumentNullException.ThrowIfNull(configure);
        Configure(draft, () => configure(new NoulCriteriaBuilder(draft)));
        question = new NoulHandle(_identity, Add(draft));
        return this;
    }

    /// <summary>Adds a Choice question over an enum's members, none of them described.</summary>
    /// <typeparam name="T">The enum whose members are the options.</typeparam>
    /// <param name="key">The question's wire key.</param>
    /// <param name="instructions">The question: text, or JSON.</param>
    /// <param name="question">The handle to read the answer with.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="key"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="instructions"/> is uninitialized.</exception>
    public QuestionSetBuilder Choice<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicFields)] T>(
        string key, DecisionContent instructions, out ChoiceHandle<T> question)
        where T : struct, Enum
    {
        var draft = EnumChoiceDraft<T>(key, instructions);
        question = new ChoiceHandle<T>(_identity, Add(draft));
        return this;
    }

    /// <summary>Adds a Choice question over an enum's members, with descriptions.</summary>
    /// <typeparam name="T">The enum whose members are the options.</typeparam>
    /// <param name="key">The question's wire key.</param>
    /// <param name="instructions">The question: text, or JSON.</param>
    /// <param name="question">The handle to read the answer with.</param>
    /// <param name="configure">Describes the options.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="key"/> or <paramref name="configure"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="instructions"/> is uninitialized.</exception>
    public QuestionSetBuilder Choice<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicFields)] T>(
        string key, DecisionContent instructions, out ChoiceHandle<T> question, Action<ChoiceOptionsBuilder<T>> configure)
        where T : struct, Enum
    {
        var draft = EnumChoiceDraft<T>(key, instructions);
        ArgumentNullException.ThrowIfNull(configure);
        Configure(draft, () => configure(new ChoiceOptionsBuilder<T>(draft, EnumOptionSet<T>.ForChoice)));
        question = new ChoiceHandle<T>(_identity, Add(draft));
        return this;
    }

    /// <summary>Adds a Choice question over string keys known at run time.</summary>
    /// <param name="key">The question's wire key.</param>
    /// <param name="instructions">The question: text, or JSON.</param>
    /// <param name="question">The handle to read the answer with.</param>
    /// <param name="configure">Adds the options.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="key"/> or <paramref name="configure"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="instructions"/> is uninitialized.</exception>
    public QuestionSetBuilder Choice(
        string key, DecisionContent instructions, out KeyedChoiceHandle question, Action<KeyedChoiceOptionsBuilder> configure)
    {
        var draft = Draft(key, QuestionKind.Choice, instructions, enumOptions: null, enumMembers: null);
        ArgumentNullException.ThrowIfNull(configure);
        Configure(draft, () => configure(new KeyedChoiceOptionsBuilder(draft)));
        question = new KeyedChoiceHandle(_identity, Add(draft));
        return this;
    }

    /// <summary>
    /// Adds a Score question over an enum's members, whose levels <paramref name="configure"/> gives lowest first, each
    /// member exactly once.
    /// </summary>
    /// <typeparam name="T">The enum whose members are the levels.</typeparam>
    /// <param name="key">The question's wire key.</param>
    /// <param name="instructions">The question: text, or JSON.</param>
    /// <param name="question">The handle to read the answer with.</param>
    /// <param name="configure">Gives the levels, in order.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="key"/> or <paramref name="configure"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="instructions"/> is uninitialized.</exception>
    public QuestionSetBuilder Score<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicFields)] T>(
        string key, DecisionContent instructions, out ScoreHandle<T> question, Action<ScoreLevelsBuilder<T>> configure)
        where T : struct, Enum
    {
        var draft = EnumScoreDraft<T>(key, instructions);
        ArgumentNullException.ThrowIfNull(configure);
        Configure(draft, () => configure(new ScoreLevelsBuilder<T>(draft, EnumOptionSet<T>.ForChoice)));
        question = new ScoreHandle<T>(_identity, Add(draft));
        return this;
    }

    /// <summary>Adds a Score question whose levels are added at run time, lowest first, keyed by index.</summary>
    /// <param name="key">The question's wire key.</param>
    /// <param name="instructions">The question: text, or JSON.</param>
    /// <param name="question">The handle to read the answer with.</param>
    /// <param name="configure">Adds the levels.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="key"/> or <paramref name="configure"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="instructions"/> is uninitialized.</exception>
    public QuestionSetBuilder Score(
        string key, DecisionContent instructions, out KeyedScoreHandle question, Action<KeyedScoreLevelsBuilder> configure)
    {
        var draft = Draft(key, QuestionKind.Score, instructions, enumOptions: null, enumMembers: null);
        ArgumentNullException.ThrowIfNull(configure);
        Configure(draft, () => configure(new KeyedScoreLevelsBuilder(draft)));
        question = new KeyedScoreHandle(_identity, Add(draft));
        return this;
    }

    /// <summary>Checks the questions against the API's rules and, when none fails, writes the set.</summary>
    /// <returns>
    /// The set, with any advice on <see cref="QuestionSet.Warnings"/>; or a <see cref="DecisionErrorKind.InvalidQuestions"/>
    /// error whose <see cref="DecisionError.Failures"/> lists every rule broken.
    /// </returns>
    public Result<QuestionSet, DecisionError> Build()
    {
        var specs = new QuestionSpec[_questions.Count];
        for (var i = 0; i < specs.Length; i++)
        {
            specs[i] = _questions[i].ToSpec();
        }

        var (failures, warnings) = QuestionValidation.Validate(specs);
        if (failures.Length > 0)
        {
            return Result<QuestionSet, DecisionError>.Failure(new DecisionError(Summary(failures), failures));
        }

        var plan = new QuestionPlan[specs.Length];
        var keys = new string[specs.Length];
        var offset = 0;
        for (var i = 0; i < specs.Length; i++)
        {
            var options = _questions[i].PlanOptions(specs[i]);
            plan[i] = new QuestionPlan(specs[i].Kind, specs[i].Key, options, offset);
            keys[i] = specs[i].Key;
            offset += options?.Count ?? 0;
        }

        return Result<QuestionSet, DecisionError>.Success(
            new QuestionSet(_identity, QuestionsWriter.Write(specs), warnings, plan, Utf8Keys.Encode(keys), offset));
    }

    private static QuestionDraft Draft(
        string key, QuestionKind kind, DecisionContent instructions, Func<QuestionSpec, IDecisionOptionKeys>? enumOptions, string[]? enumMembers)
    {
        ArgumentNullException.ThrowIfNull(key);
        DecisionContent.EnsureInitialized(instructions, nameof(instructions));
        return new QuestionDraft(key, kind, instructions, enumOptions, enumMembers);
    }

    // Every distinct member is an option, in declaration order as the generator sends them, undescribed until Describe.
    private static QuestionDraft EnumChoiceDraft<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicFields)] T>(
        string key, DecisionContent instructions)
        where T : struct, Enum
    {
        var options = EnumOptionSet<T>.ForChoice;
        var draft = Draft(key, QuestionKind.Choice, instructions, static _ => EnumOptionSet<T>.ForChoice, enumMembers: null);
        for (var i = 0; i < options.Count; i++)
        {
            draft.Options.Add(new OptionSpec(options.KeyAt(i), options.NameAt(i), null, i));
        }

        return draft;
    }

    // No level until Level adds one; answers are read through the levels in the order they were given.
    private static QuestionDraft EnumScoreDraft<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicFields)] T>(
        string key, DecisionContent instructions)
        where T : struct, Enum
    {
        var options = EnumOptionSet<T>.ForChoice;
        var members = new string[options.Count];
        for (var i = 0; i < members.Length; i++)
        {
            members[i] = options.NameAt(i);
        }

        return Draft(key, QuestionKind.Score, instructions, static spec => EnumOptionSet<T>.ForChoice.Levels(LevelMembers(spec)), members);
    }

    private static int[] LevelMembers(QuestionSpec spec)
    {
        var members = new int[spec.Options.Length];
        for (var i = 0; i < members.Length; i++)
        {
            members[i] = spec.Options[i].Member;
        }

        return members;
    }

    private static string Summary(QuestionFailure[] failures)
        => $"The question set breaks {failures.Length.ToString(CultureInfo.InvariantCulture)} rule(s). {failures[0].Rule}: {failures[0].Message}";

    // A configurator works only while its callback runs, even when the callback throws, so a stored one cannot change
    // a question already added.
    private static void Configure(QuestionDraft draft, Action callback)
    {
        try
        {
            callback();
        }
        finally
        {
            draft.Close();
        }
    }

    private int Add(QuestionDraft draft)
    {
        _questions.Add(draft);
        return _questions.Count - 1;
    }
}
