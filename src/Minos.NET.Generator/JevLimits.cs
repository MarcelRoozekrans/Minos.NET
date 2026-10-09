namespace Minos.Generator;

/// <summary>
/// The limits the analyzers check at compile time and <c>JevQuestionSetBuilder.Build</c> checks at run time. One file,
/// compiled into the generator and linked into the analyzers and the library, so both sides always agree.
/// </summary>
internal static class JevLimits
{
    /// <summary>The fewest options a Choice, or levels a Score, may have: the API's schema rejects none (JEV001, JEV002).</summary>
    public const int MinimumOptions = 1;

    /// <summary>The fewest Score levels the API sketch's guidance recommends (JEV005); the schema sets no bound.</summary>
    public const int MinimumScoreLevels = 2;

    /// <summary>The most Score levels the API sketch's guidance recommends (JEV005); the schema sets no bound.</summary>
    public const int MaximumScoreLevels = 10;

    /// <summary>The most Choice options the API sketch's guidance recommends (JEV005); the schema sets no bound.</summary>
    public const int MaximumChoiceOptions = 255;

    /// <summary>
    /// The deepest nesting a built set's JSON instructions or JSON description may have (JEV108). Only the run-time
    /// builder checks it: declared sets are text only. System.Text.Json reads a request with its default MaxDepth of 64,
    /// and a criterion description sits 4 levels deep in it: the request object, <c>questions</c>, the question and its
    /// <c>criteria</c>.
    /// </summary>
    public const int MaximumJsonDepth = 60;
}
