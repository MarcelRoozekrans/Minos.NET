using System.Net;
using System.Text.Json;

namespace Minos.AotSmoke;

/// <summary>A typed evaluation whose response lacks an answer, under Native AOT: the protocol names the missing question.</summary>
internal static class MissingAnswerChecks
{
    private const string ResponseWithoutTeam = """{"model":"jev-1.13.0","answers":{"requests_credentials":{"type":"noul","noul":0.1},"urgency":{"type":"score","score":1.9,"legend":{"0":"Low","1":"Medium","2":"High"},"probabilities":{"0":0.0,"1":0.1,"2":0.9},"confidence":0.8}},"usage":{"input_tokens":296,"output_tokens":20}}""";

    public static async Task MissingAnswerNamesTheQuestion()
    {
        using var http = Program.Http(HttpStatusCode.OK, ResponseWithoutTeam);
        using var client = new DecisionClient(http, Program.Options());

        var result = await client.EvaluateAsync<SmokeTriage>(SmokeAnswers.State).ConfigureAwait(false);

        Program.Check(
            result.IsFailure
                && result.Error.Kind == DecisionErrorKind.InvalidResponse
                && result.Error.Exception is JsonException exception
                && exception.Message.Contains("'team'", StringComparison.Ordinal),
            "a typed evaluation whose response lacks an answer is InvalidResponse naming the question under Native AOT");
    }
}
