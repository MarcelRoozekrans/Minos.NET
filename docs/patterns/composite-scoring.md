# Composite scoring

Instead of asking one large question such as "is this a good candidate?", ask several small ones that each have an
answer, then combine the answers yourself. Each Score is an atomic judgement that is easy to check by hand. The
combination is a weighted sum in your own code, so the policy is yours: it is visible, testable and easy to change.

TypeSafe describes the pattern in [Composite scoring](https://docs.typesafe.ai/patterns/composite-scoring). This page
shows it with typed Scores and `Normalized`.

## Why normalize

A Score's `Expected` is a level index weighted by probability: 3.0 on a five-level rubric is high, while 3.0 on a
three-level rubric does not exist. Adding raw values from rubrics of different sizes mixes scales. `Normalized` divides
`Expected` by the index of the top level, so every Score lands on 0 to 1 whatever its rubric, and weights then mean
the same thing for all of them.

## The questions

Four Scores. Two use a five-level rubric and two a three-level one, on purpose.

<!-- snippet: CompositeScoringQuestions -->
```cs
public enum Depth
{
    [Level("None")]
    None,

    [Level("Has read about it")]
    Aware,

    [Level("Has used it on real work")]
    Working,

    [Level("Has led work with it")]
    Strong,

    [Level("Recognised expert")]
    Expert,
}

public enum Breadth
{
    [Level("One area")]
    Narrow,

    [Level("A few related areas")]
    Moderate,

    [Level("Many unrelated areas")]
    Wide,
}

// Four atomic judgements. Two use a 5-level rubric and two a 3-level one, so their raw Expected values
// are on different scales; Normalized puts every one of them on 0 to 1.
[JevQuestions]
public partial record CandidateAssessment
{
    [Score("How deep is the candidate's C# experience?")]
    public partial Score<Depth> CSharp { get; }

    [Score("How much system design has the candidate done?")]
    public partial Score<Depth> SystemDesign { get; }

    [Score("How much has the candidate led other people?")]
    public partial Score<Breadth> Leadership { get; }

    [Score("How broad is the candidate's experience?")]
    public partial Score<Breadth> Generalist { get; }
}
```
<!-- endSnippet -->

## The weights

Two roles read the same four answers with different weights. A new role is a new method, not a new request.

<!-- snippet: CompositeScoringWeights -->
```cs
public static class CandidateScoring
{
    // The weights are the hiring policy. They live in code, they sum to 1, and changing them needs no new request.
    public static double IndividualContributor(CandidateAssessment a)
        => (0.40 * a.CSharp.Normalized)
         + (0.40 * a.SystemDesign.Normalized)
         + (0.10 * a.Leadership.Normalized)
         + (0.10 * a.Generalist.Normalized);

    public static double EngineeringManager(CandidateAssessment a)
        => (0.15 * a.CSharp.Normalized)
         + (0.20 * a.SystemDesign.Normalized)
         + (0.40 * a.Leadership.Normalized)
         + (0.25 * a.Generalist.Normalized);
}
```
<!-- endSnippet -->

## C# notes

- `Normalized` runs from 0 to 1, and reading it allocates nothing.
- Make each set of weights sum to 1, so the composite stays on 0 to 1 and thresholds on it keep their meaning.
- Changing the policy means changing the weights. The request, and so the cost of the call, stays the same.
- `KeyedScore.Normalized` does the same for question sets built at run time.
