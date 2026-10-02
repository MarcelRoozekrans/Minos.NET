namespace ZeroAlloc.Jev.Docs.Tests;

public sealed class CompositeScoringTests
{
    private const string Response = """
        {
          "model": "jev-1.13.0",
          "answers": {
            "c_sharp": { "type": "score", "score": 3.0, "legend": { "0": "None", "1": "Has read about it", "2": "Has used it on real work", "3": "Has led work with it", "4": "Recognised expert" }, "probabilities": { "0": 0.0, "1": 0.0, "2": 0.1, "3": 0.8, "4": 0.1 }, "confidence": 0.8 },
            "system_design": { "type": "score", "score": 2.0, "legend": { "0": "None", "1": "Has read about it", "2": "Has used it on real work", "3": "Has led work with it", "4": "Recognised expert" }, "probabilities": { "0": 0.0, "1": 0.1, "2": 0.8, "3": 0.1, "4": 0.0 }, "confidence": 0.75 },
            "leadership": { "type": "score", "score": 1.0, "legend": { "0": "One area", "1": "A few related areas", "2": "Many unrelated areas" }, "probabilities": { "0": 0.1, "1": 0.8, "2": 0.1 }, "confidence": 0.7 },
            "generalist": { "type": "score", "score": 1.0, "legend": { "0": "One area", "1": "A few related areas", "2": "Many unrelated areas" }, "probabilities": { "0": 0.1, "1": 0.8, "2": 0.1 }, "confidence": 0.7 }
          },
          "usage": { "input_tokens": 640, "output_tokens": 30 }
        }
        """;

    [Fact]
    public async Task Weights_CombineNormalizedScores()
    {
        var candidate = await CannedJev.EvaluateAsync<CandidateAssessment>(Response, "CV text ...");

        Assert.Equal(0.75, candidate.CSharp.Normalized, 12);
        Assert.Equal(0.5, candidate.Leadership.Normalized, 12);
        Assert.Equal(0.60, CandidateScoring.IndividualContributor(candidate), 12);
        Assert.Equal(0.5375, CandidateScoring.EngineeringManager(candidate), 12);
    }
}
