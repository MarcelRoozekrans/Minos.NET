namespace Minos.Docs.Tests;

public sealed class CompositeScoringTests
{
    // Each score equals its probability-weighted level:
    //   correctness   2 × 0.1 + 3 × 0.6 + 4 × 0.3 = 3.2, on 5 levels
    //   test_coverage 1 × 0.2 + 2 × 0.6 + 3 × 0.2 = 2.0, on 5 levels
    //   readability   1 × 0.3 + 2 × 0.6           = 1.5, on 3 levels
    //   scope_focus   1 × 0.3 + 2 × 0.1           = 0.5, on 3 levels
    private const string Response = """
        {
          "model": "jev-1.13.0",
          "answers": {
            "correctness": { "type": "score", "score": 3.2, "legend": { "0": "Broken: it does not do what it claims", "1": "Major defects", "2": "Minor defects", "3": "Correct for the common cases", "4": "Correct, edge cases included" }, "probabilities": { "0": 0.0, "1": 0.0, "2": 0.1, "3": 0.6, "4": 0.3 }, "confidence": 0.76 },
            "test_coverage": { "type": "score", "score": 2.0, "legend": { "0": "No tests", "1": "A token test", "2": "The happy path only", "3": "The main paths and their failures", "4": "Thorough, edge cases included" }, "probabilities": { "0": 0.0, "1": 0.2, "2": 0.6, "3": 0.2, "4": 0.0 }, "confidence": 0.72 },
            "readability": { "type": "score", "score": 1.5, "legend": { "0": "Hard to follow", "1": "Readable with effort", "2": "Reads easily" }, "probabilities": { "0": 0.1, "1": 0.3, "2": 0.6 }, "confidence": 0.7 },
            "scope_focus": { "type": "score", "score": 0.5, "legend": { "0": "Mixes unrelated changes", "1": "Mostly one change, with some extras", "2": "One focused change" }, "probabilities": { "0": 0.6, "1": 0.3, "2": 0.1 }, "confidence": 0.68 }
          },
          "usage": { "input_tokens": 1250, "output_tokens": 30 }
        }
        """;

    [Fact]
    public async Task Weights_CombineNormalizedScores()
    {
        var review = await CannedJev.EvaluateAsync<PullRequestReview>(Response, "diff --git a/src/Cart.cs b/src/Cart.cs ...");

        // Normalized divides by the top level's index: 4 on a 5-level rubric, 2 on a 3-level one.
        //   correctness 3.2 / 4 = 0.80, test_coverage 2.0 / 4 = 0.50, readability 1.5 / 2 = 0.75, scope_focus 0.5 / 2 = 0.25
        Assert.Equal(0.80, review.Correctness.Normalized, 12);
        Assert.Equal(0.50, review.TestCoverage.Normalized, 12);
        Assert.Equal(0.75, review.Readability.Normalized, 12);
        Assert.Equal(0.25, review.ScopeFocus.Normalized, 12);

        // Ready to merge:    0.45 × 0.80 + 0.35 × 0.50 + 0.10 × 0.75 + 0.10 × 0.25
        //                  = 0.360      + 0.175      + 0.075      + 0.025       = 0.635
        Assert.Equal(0.635, PullRequestScoring.ReadyToMerge(review), 12);

        // Good first review: 0.15 × 0.80 + 0.10 × 0.50 + 0.40 × 0.75 + 0.35 × 0.25
        //                  = 0.120      + 0.050      + 0.300      + 0.0875      = 0.5575
        Assert.Equal(0.5575, PullRequestScoring.GoodFirstReview(review), 12);
    }
}
