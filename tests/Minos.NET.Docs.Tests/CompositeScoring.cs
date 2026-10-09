namespace Minos.Docs.Tests;

#region CompositeScoringQuestions
public enum CorrectnessLevel
{
    [Level("Broken: it does not do what it claims")]
    Broken,

    [Level("Major defects")]
    MajorDefects,

    [Level("Minor defects")]
    MinorDefects,

    [Level("Correct for the common cases")]
    CommonCases,

    [Level("Correct, edge cases included")]
    EdgeCases,
}

public enum CoverageLevel
{
    [Level("No tests")]
    None,

    [Level("A token test")]
    Token,

    [Level("The happy path only")]
    HappyPath,

    [Level("The main paths and their failures")]
    MainPaths,

    [Level("Thorough, edge cases included")]
    Thorough,
}

public enum ReadabilityLevel
{
    [Level("Hard to follow")]
    Hard,

    [Level("Readable with effort")]
    WithEffort,

    [Level("Reads easily")]
    Easy,
}

public enum FocusLevel
{
    [Level("Mixes unrelated changes")]
    Mixed,

    [Level("Mostly one change, with some extras")]
    Mostly,

    [Level("One focused change")]
    Focused,
}

// Four atomic judgements about a pull request. Two use a 5-level rubric and two a 3-level one, so their raw
// Expected values are on different scales; Normalized puts every one of them on 0 to 1.
[JevQuestions]
public partial record PullRequestReview
{
    [Score("How correct is the change?")]
    public partial Score<CorrectnessLevel> Correctness { get; }

    [Score("How well do the tests cover the change?")]
    public partial Score<CoverageLevel> TestCoverage { get; }

    [Score("How easy is the diff to read?")]
    public partial Score<ReadabilityLevel> Readability { get; }

    [Score("How focused is the pull request on a single change?")]
    public partial Score<FocusLevel> ScopeFocus { get; }
}
#endregion

#region CompositeScoringWeights
public static class PullRequestScoring
{
    // Each policy is a set of weights in code that sums to 1. Changing a policy, or adding one, needs no new request.

    // Ready to merge: what matters is that it works and that the tests prove it.
    public static double ReadyToMerge(PullRequestReview r)
        => (0.45 * r.Correctness.Normalized)
         + (0.35 * r.TestCoverage.Normalized)
         + (0.10 * r.Readability.Normalized)
         + (0.10 * r.ScopeFocus.Normalized);

    // A good first review for a new teammate: small, focused and easy to read matters more than polish.
    public static double GoodFirstReview(PullRequestReview r)
        => (0.15 * r.Correctness.Normalized)
         + (0.10 * r.TestCoverage.Normalized)
         + (0.40 * r.Readability.Normalized)
         + (0.35 * r.ScopeFocus.Normalized);
}
#endregion
