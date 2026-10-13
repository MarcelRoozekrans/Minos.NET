using System.Net;
using System.Text;
using System.Text.Json;
using Minos.SystemOne;
using ZeroAlloc.Results;

namespace Minos.AotSmoke;

/// <summary>
/// The smoke app's shared states and answers: the text and JSON states the checks send, the generated enum answers
/// and the built keyed answers they read, and the triage result they expect.
/// </summary>
internal static class SmokeAnswers
{
    /// <summary>The text state every evaluation sends.</summary>
    public const string State = "Help! My payouts have been failing for 3 days.";

    /// <summary>The same message as a JSON object state.</summary>
    public const string JsonState = """{"subject":"Payouts failing","body":"Help! My payouts have been failing for 3 days."}""";

    /// <summary>Whether <paramref name="result"/> is the triage every canned triage response answers.</summary>
    public static bool IsTriage(Result<SmokeTriage, DecisionError> result)
        => result.IsSuccess
            && !result.Value.RequestsCredentials.Value
            && result.Value.Team.Value == Team.Account
            && result.Value.Urgency.Value == Urgency.High;

    /// <summary>The triage answers, read through the generated <c>SmokeTriage.Create</c> over a canned handler.</summary>
    public static Task<SmokeTriage> TriageAsync() => TriageAsync(Program.TriageResponse);

    /// <summary>The triage whose team is billing and whose urgency is low, a second non-default value to compare against.</summary>
    public static Task<SmokeTriage> OtherTriageAsync()
        => TriageAsync(Program.TriageResponse
            .Replace("\"billing\":0.2,\"account\":0.8", "\"billing\":0.8,\"account\":0.2", StringComparison.Ordinal)
            .Replace("\"score\":1.9", "\"score\":0.1", StringComparison.Ordinal)
            .Replace("\"0\":0.0,\"1\":0.1,\"2\":0.9", "\"0\":0.9,\"1\":0.1,\"2\":0.0", StringComparison.Ordinal));

    /// <summary>The triage answers read from <paramref name="responseJson"/> over a canned handler.</summary>
    public static async Task<SmokeTriage> TriageAsync(string responseJson)
    {
        using var http = Program.Http(HttpStatusCode.OK, responseJson);
        using var client = new SystemOneDecisionClient(http, Program.Options());
        var result = await client.EvaluateAsync<SmokeTriage>(State).ConfigureAwait(false);
        return result.IsSuccess ? result.Value : throw new InvalidOperationException("The canned triage did not parse: " + result.Error.Message);
    }

    /// <summary>The keyed Choice answer of <see cref="SmokeBuiltSet.Full"/>, evaluated over a canned handler.</summary>
    public static async Task<KeyedChoice> ProductAsync()
    {
        var set = SmokeBuiltSet.Full(out _, out _, out var product, out _);
        return (await EvaluateAsync(set, SmokeBuiltSet.ResponseJson).ConfigureAwait(false)).Get(product);
    }

    /// <summary>The keyed Choice answer of <see cref="SmokeBuiltSet.Full"/> when the model picks the other option.</summary>
    public static async Task<KeyedChoice> OtherProductAsync()
    {
        var set = SmokeBuiltSet.Full(out _, out _, out var product, out _);
        var json = SmokeBuiltSet.ResponseJson.Replace("\"choice\":\"pro-plan\",\"probabilities\":{\"pro-plan\":0.9,\"team-plan\":0.1}", "\"choice\":\"team-plan\",\"probabilities\":{\"pro-plan\":0.1,\"team-plan\":0.9}", StringComparison.Ordinal);
        return (await EvaluateAsync(set, json).ConfigureAwait(false)).Get(product);
    }

    /// <summary>The keyed Score answer of <see cref="SmokeBuiltSet.KeyedRisk"/>, evaluated over a canned handler.</summary>
    public static async Task<KeyedScore> RiskAsync()
    {
        var set = SmokeBuiltSet.KeyedRisk(out var risk);
        return (await EvaluateAsync(set, SmokeBuiltSet.KeyedRiskResponseJson).ConfigureAwait(false)).Get(risk);
    }

    /// <summary>The keyed Score answer of <see cref="SmokeBuiltSet.KeyedRisk"/> when the model rates the risk lower.</summary>
    public static async Task<KeyedScore> OtherRiskAsync()
    {
        var set = SmokeBuiltSet.KeyedRisk(out var risk);
        var json = SmokeBuiltSet.KeyedRiskResponseJson
            .Replace("\"score\":2.4", "\"score\":0.4", StringComparison.Ordinal)
            .Replace("\"0\":0.0,\"1\":0.1,\"2\":0.4,\"3\":0.5", "\"0\":0.6,\"1\":0.3,\"2\":0.1,\"3\":0.0", StringComparison.Ordinal);
        return (await EvaluateAsync(set, json).ConfigureAwait(false)).Get(risk);
    }

    /// <summary>The answers to <paramref name="set"/>, from <paramref name="responseJson"/> over a canned handler.</summary>
    public static async Task<QuestionSetAnswers> EvaluateAsync(QuestionSet set, string responseJson)
    {
        using var http = Program.Http(HttpStatusCode.OK, responseJson);
        using var client = new SystemOneDecisionClient(http, Program.Options());
        var result = await client.EvaluateAsync(set, State).ConfigureAwait(false);
        return result.IsSuccess ? result.Value : throw new InvalidOperationException("The canned answers did not parse: " + result.Error.Message);
    }
}
