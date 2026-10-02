using System.Globalization;
using System.Net;
using System.Text;
using ZeroAlloc.Jev.Samples.Guardrails;

namespace ZeroAlloc.Jev.Samples.Tests;

/// <summary>Builds a <see cref="MessageScreen"/> from chosen answers, through a real <see cref="JevClient"/> over a stub handler.</summary>
internal static class CannedScreen
{
    public static async Task<MessageScreen> Of(
        double overrides = 0,
        double personalData = 0,
        double advice = 0,
        double abusive = 0,
        double severity = 0)
    {
        var json = string.Create(
            CultureInfo.InvariantCulture,
            $$$$"""
            {"model":"canned","answers":{
              "overrides_instructions":{"type":"noul","noul":{{{{overrides}}}}},
              "shares_personal_data":{"type":"noul","noul":{{{{personalData}}}}},
              "asks_for_professional_advice":{"type":"noul","noul":{{{{advice}}}}},
              "is_abusive":{"type":"noul","noul":{{{{abusive}}}}},
              "severity":{"type":"score","score":{{{{severity}}}},
                "legend":{"0":"None: nothing harmful","1":"Mild: rude or off-topic","2":"Serious: harmful if acted on","3":"Dangerous: a risk to someone's safety"},
                "probabilities":{"0":0.25,"1":0.25,"2":0.25,"3":0.25},"confidence":0.9}},
             "usage":{"input_tokens":1,"output_tokens":1}}
            """);
        using var http = new HttpClient(new Handler(json)) { BaseAddress = new Uri("https://canned.example/api/") };
        using var jev = new JevClient(http, new JevClientOptions { ApiKey = "canned-key", MaxRetries = 0 });
        var result = await jev.EvaluateAsync<MessageScreen>("any message", CancellationToken.None);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Kind + ": " + result.Error.Message : null);
        return result.Value;
    }

    private sealed class Handler(string json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
    }
}
