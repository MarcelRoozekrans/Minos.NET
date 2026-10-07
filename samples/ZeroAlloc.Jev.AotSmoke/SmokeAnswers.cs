using System.Net;
using System.Text;
using System.Text.Json;

namespace ZeroAlloc.Jev.AotSmoke;

/// <summary>Parsed answers for the answer-type checks: generated enum answers, and built keyed answers.</summary>
internal static class SmokeAnswers
{
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
        var result = await client.EvaluateAsync(set, "Help! My payouts have been failing for 3 days.").ConfigureAwait(false);
        return result.IsSuccess ? result.Value : throw new InvalidOperationException("The canned answers did not parse: " + result.Error.Message);
    }
}
