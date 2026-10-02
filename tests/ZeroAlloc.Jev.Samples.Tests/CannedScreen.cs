using System.Globalization;
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
        return await Canned.EvaluateAsync<MessageScreen>(json);
    }
}
