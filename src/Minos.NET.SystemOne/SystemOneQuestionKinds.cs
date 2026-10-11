namespace Minos.SystemOne;

/// <summary>The question kinds a provider accepts, as flags for <see cref="SystemOneCapabilities.QuestionKinds"/>.</summary>
/// <remarks>Binds from configuration as a comma-separated list, such as <c>"Noul, Choice"</c>.</remarks>
[Flags]
public enum SystemOneQuestionKinds
{
    /// <summary>No question kind. Not a valid capability; it is here as the empty value of the flags.</summary>
    None = 0,

    /// <summary>Yes-or-no questions, answered with a probability.</summary>
    Noul = 1,

    /// <summary>Questions with one answer from a fixed set of options.</summary>
    Choice = 2,

    /// <summary>Questions rated on ordered levels.</summary>
    Score = 4,

    /// <summary>Every question kind: <see cref="Noul"/>, <see cref="Choice"/> and <see cref="Score"/>.</summary>
    All = Noul | Choice | Score,
}
