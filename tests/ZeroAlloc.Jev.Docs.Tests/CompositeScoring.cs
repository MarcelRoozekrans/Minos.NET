namespace ZeroAlloc.Jev.Docs.Tests;

#region CompositeScoringQuestions
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
#endregion

#region CompositeScoringWeights
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
#endregion
