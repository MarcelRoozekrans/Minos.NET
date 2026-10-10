namespace Minos;

/// <summary>A set of questions declared as a C# type. The <c>[Questions]</c> source generator implements it.</summary>
/// <typeparam name="TSelf">The implementing type.</typeparam>
public interface IQuestionSet<TSelf>
    where TSelf : IQuestionSet<TSelf>
{
    /// <summary>Gets the provider-neutral definition of the set's questions.</summary>
    static abstract QuestionSetDefinition Definition { get; }

    /// <summary>Creates the typed answers from the slots a protocol read for <see cref="Definition"/>.</summary>
    /// <param name="answers">One slot per question, in <see cref="Definition"/> order.</param>
    /// <returns>The typed answers.</returns>
    static abstract TSelf Create(AnswerSlots answers);
}

/// <summary>
/// A <see cref="IQuestionSet{TSelf}"/> linked to the state type <c>EvaluateAsync&lt;T, TState&gt;</c> accepts
/// alongside it. This is a marker interface with no members: <typeparamref name="TState"/> only participates in
/// overload resolution and static typing for typed evaluation. The <c>[Questions(State = typeof(TState))]</c>
/// source generator implements it.
/// </summary>
/// <typeparam name="TSelf">The implementing type.</typeparam>
/// <typeparam name="TState">The state type this question set evaluates against.</typeparam>
public interface IQuestionSet<TSelf, TState> : IQuestionSet<TSelf>
    where TSelf : IQuestionSet<TSelf, TState>;
