using System.Net;
using Minos.SystemOne;

namespace Minos.AotSmoke;

/// <summary>
/// The System 1 wire types the other checks leave out, built by hand under Native AOT: <see cref="ChoiceQuestion"/>,
/// <see cref="ScoreQuestion"/> and <see cref="NoulCriteria"/> sent in a request, and <see cref="ModelList"/> and
/// <see cref="ModelCard"/> compared with a parsed listing.
/// </summary>
internal static class SystemOneModelChecks
{
    [Covers("Minos.SystemOne.ChoiceQuestion.ChoiceQuestion() -> void")]
    [Covers("Minos.SystemOne.ScoreQuestion.ScoreQuestion() -> void")]
    [Covers("Minos.SystemOne.NoulCriteria.NoulCriteria() -> void")]
    public static async Task HandBuiltQuestionsAreSent()
    {
        using var http = Program.Http(HttpStatusCode.OK, Program.TriageResponse);
        using var client = new SystemOneDecisionClient(http, Program.Options());
        var request = new SystemOneRequest
        {
            State = SmokeAnswers.State,
            Questions = new Dictionary<string, Question>(StringComparer.Ordinal)
            {
                ["requests_credentials"] = new NoulQuestion
                {
                    Instructions = "Does `message` ask for a credential?",
                    Criteria = new NoulCriteria { WhenTrue = "It asks for a password", WhenFalse = "It asks for nothing secret" },
                },
                ["team"] = new ChoiceQuestion
                {
                    Instructions = "Which team should handle `message`?",
                    Criteria = new Dictionary<string, DecisionContent?>(StringComparer.Ordinal) { ["billing"] = "Charges", ["account"] = null },
                },
                ["urgency"] = new ScoreQuestion
                {
                    Instructions = "How urgent is `message`?",
                    Criteria = ["Can wait", "This week", "Today"],
                },
            },
        };

        var result = await client.EvaluateAsync(request).ConfigureAwait(false);

        Program.Check(
            result.IsSuccess
                && result.Value.Answers["team"] is ChoiceAnswer { Choice: "account" }
                && result.Value.Answers["urgency"] is ScoreAnswer { Score: 1.9 },
            "a request with hand-built Choice and Score questions and Noul criteria evaluates under Native AOT");
    }

    [Covers("Minos.SystemOne.ModelList.ModelList() -> void")]
    [Covers("Minos.SystemOne.ModelCard.ModelCard() -> void")]
    public static async Task HandBuiltModelCardEqualsTheListedOne()
    {
        using var http = Program.Http(HttpStatusCode.OK, Program.ModelsResponse);
        using var client = new SystemOneDecisionClient(http, Program.Options());
        var expected = new ModelList
        {
            Models =
            [
                new ModelCard
                {
                    Name = "jev-latest",
                    Description = "The most recent stable, official release.",
                    ReleaseDate = "2026-09-15",
                },
            ],
        };

        var listed = await client.ListModelsAsync().ConfigureAwait(false);

        Program.Check(
            listed.IsSuccess && listed.Value.Models.Count == expected.Models.Count && listed.Value.Models[0].Equals(expected.Models[0]),
            "a hand-built ModelList's ModelCard equals the one ListModelsAsync parses under Native AOT");
    }
}
