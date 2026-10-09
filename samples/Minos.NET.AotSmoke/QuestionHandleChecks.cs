namespace Minos.AotSmoke;

/// <summary>
/// The question handles' parameterless constructors under Native AOT: <see cref="NoulHandle"/>,
/// <see cref="ChoiceHandle{T}"/>, <see cref="ScoreHandle{T}"/>, <see cref="KeyedChoiceHandle"/> and
/// <see cref="KeyedScoreHandle"/>. A default handle belongs to no question set, so <see cref="Answers"/> rejects it.
/// </summary>
internal static class QuestionHandleChecks
{
    [Covers("Minos.NoulHandle.NoulHandle() -> void")]
    [Covers("Minos.ChoiceHandle<T>.ChoiceHandle() -> void")]
    [Covers("Minos.ScoreHandle<T>.ScoreHandle() -> void")]
    [Covers("Minos.KeyedChoiceHandle.KeyedChoiceHandle() -> void")]
    [Covers("Minos.KeyedScoreHandle.KeyedScoreHandle() -> void")]
    public static async Task DefaultHandlesAreRejected()
    {
        var answers = await SmokeAnswers
            .EvaluateAsync(SmokeBuiltSet.Full(out _, out _, out _, out _), SmokeBuiltSet.ResponseJson)
            .ConfigureAwait(false);

        Program.Check(
            SmokeAssert.Throws<ArgumentException>(() => answers.Get(new NoulHandle()))
                && SmokeAssert.Throws<ArgumentException>(() => answers.Get(new ChoiceHandle<Team>()))
                && SmokeAssert.Throws<ArgumentException>(() => answers.Get(new ScoreHandle<Urgency>()))
                && SmokeAssert.Throws<ArgumentException>(() => answers.Get(new KeyedChoiceHandle()))
                && SmokeAssert.Throws<ArgumentException>(() => answers.Get(new KeyedScoreHandle())),
            "Answers.Get rejects a default handle of every kind under Native AOT");
    }
}
