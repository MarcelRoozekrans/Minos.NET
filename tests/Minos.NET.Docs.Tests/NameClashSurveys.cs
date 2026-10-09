namespace Shop.Surveys;

// An application's own survey types, for the page's note on names that clash with Minos.Question and Minos.Answer.
// They sit outside the Minos namespace on purpose: code inside it would find Minos's types first and never clash.
public sealed class Question
{
    public required string Key { get; init; }

    public required string Text { get; init; }
}

public sealed class Survey
{
    public required IReadOnlyList<Question> Questions { get; init; }
}
