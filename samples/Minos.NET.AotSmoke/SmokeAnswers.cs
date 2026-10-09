using System.Net;
using System.Text;
using System.Text.Json;
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
    public static bool IsTriage(Result<SmokeTriage, JevError> result)
        => result.IsSuccess
            && !result.Value.RequestsCredentials.Value
            && result.Value.Team.Value == Team.Account
            && result.Value.Urgency.Value == Urgency.High;

    /// <summary>The triage answers, parsed by the generated <c>SmokeTriage.Parse</c>.</summary>
    public static SmokeTriage Triage()
    {
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(Program.TriageAnswers));
        reader.Read();
        return SmokeTriage.Parse(ref reader);
    }

    /// <summary>The keyed Choice answer of <see cref="SmokeBuiltSet.Full"/>, evaluated over a canned handler.</summary>
    public static async Task<KeyedChoice> ProductAsync()
    {
        var set = SmokeBuiltSet.Full(out _, out _, out var product, out _);
        return (await EvaluateAsync(set, SmokeBuiltSet.ResponseJson).ConfigureAwait(false)).Get(product);
    }

    /// <summary>The keyed Score answer of <see cref="SmokeBuiltSet.KeyedRisk"/>, evaluated over a canned handler.</summary>
    public static async Task<KeyedScore> RiskAsync()
    {
        var set = SmokeBuiltSet.KeyedRisk(out var risk);
        return (await EvaluateAsync(set, SmokeBuiltSet.KeyedRiskResponseJson).ConfigureAwait(false)).Get(risk);
    }

    /// <summary>The answers to <paramref name="set"/>, from <paramref name="responseJson"/> over a canned handler.</summary>
    public static async Task<JevAnswers> EvaluateAsync(JevQuestionSet set, string responseJson)
    {
        using var http = Program.Http(HttpStatusCode.OK, responseJson);
        using var client = new JevClient(http, Program.Options());
        var result = await client.EvaluateAsync(set, State).ConfigureAwait(false);
        return result.IsSuccess ? result.Value : throw new InvalidOperationException("The canned answers did not parse: " + result.Error.Message);
    }
}
