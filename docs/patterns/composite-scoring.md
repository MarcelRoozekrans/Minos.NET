# Composite scoring

Instead of asking one large question such as "is this pull request good?", ask several small ones that each have an
answer, then combine the answers yourself. Each Score is an atomic judgement that is easy to check by hand. The
combination is a weighted sum in your own code, so the policy is yours: it is visible, testable and easy to change.

TypeSafe describes the pattern in [Composite scoring](https://docs.typesafe.ai/patterns/composite-scoring). This page
shows it with typed Scores and `Normalized`.

## Why normalize

A Score's `Expected` is a level index weighted by probability: 2.0 on a five-level rubric is the middle, while 2.0 on a
three-level rubric is the top. Adding raw values from rubrics of different sizes mixes scales. `Normalized` divides
`Expected` by the index of the top level, so every Score lands on 0 to 1 whatever its rubric, and weights then mean
the same thing for all of them.

## The questions

A pull request gets four Scores. Two use a five-level rubric and two a three-level one, on purpose.

<!-- snippet: CompositeScoringQuestions -->
```cs
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
```
<!-- endSnippet -->

## The weights

Two policies read the same four answers with different weights. One asks whether the pull request is ready to merge;
the other asks whether it would make a good first review for a new teammate. A new policy is a new method, not a new
request.

<!-- snippet: CompositeScoringWeights -->
```cs
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
```
<!-- endSnippet -->

## C# notes

- `Normalized` runs from 0 to 1, and reading it allocates nothing.
- Make each set of weights sum to 1, so the composite stays on 0 to 1 and thresholds on it keep their meaning.
- Changing the policy means changing the weights. The request, and so the cost of the call, stays the same.
- `KeyedScore.Normalized` does the same for question sets built at run time; the
  [fan-out](fan-out.md#built-at-run-time) guide shows how to build one.
