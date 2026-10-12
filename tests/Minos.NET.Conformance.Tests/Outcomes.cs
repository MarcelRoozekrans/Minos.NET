using System.Text.Json;

namespace Minos.Conformance.Tests;

/// <summary>Hand-made outcomes for the check tests: a wire body, and what Minos read, made separately so a test can make them disagree.</summary>
internal static class Outcomes
{
    public static ConformanceTarget SystemOneTarget { get; } = Target(ConformanceWire.SystemOne, "jev-latest", ModelRule.TypeSafe);

    public static ConformanceTarget DecisionsTarget { get; } = Target(ConformanceWire.Decisions, "gpt-6-luna", modelRule: null);

    public static ConformanceTarget Target(ConformanceWire wire, string model, ModelRule? modelRule)
        => new("test", wire, model, modelRule, _ => throw new InvalidOperationException("The check tests never create a client."));

    /// <summary>A probe that answered: one exchange with <paramref name="body"/>, and Minos's own reading.</summary>
    public static ProbeOutcome Answered(ConformanceProbe probe, string body, DecisionResponse? response, int status = 200, params (string Name, string Value)[] headers)
        => new(probe, [Exchange(probe, status, body, headers)], response, error: null);

    /// <summary>A probe that failed: one exchange with <paramref name="body"/>, and Minos's error.</summary>
    public static ProbeOutcome Failed(ConformanceProbe probe, int status, string body, DecisionError? error, params (string Name, string Value)[] headers)
        => new(probe, [Exchange(probe, status, body, headers)], response: null, error);

    public static ConformanceExchange Exchange(ConformanceProbe probe, int status, string body, params (string Name, string Value)[] headers)
        => ConformanceExchange.FromHttp(
            probe.Id,
            requestBody: null,
            status,
            "application/json",
            headers.Length == 0 ? null : headers.ToDictionary(h => h.Name, h => h.Value, StringComparer.Ordinal),
            body);

    public static DecisionError Error(DecisionErrorKind kind, int status, string body, string? requestId = null, TimeSpan? retryAfter = null)
    {
        using var document = JsonDocument.Parse(body);
        return new DecisionError(kind, "The API returned HTTP " + status + ".")
        {
            StatusCode = status,
            Detail = document.RootElement.Clone(),
            RequestId = requestId,
            RetryAfter = retryAfter,
        };
    }

    public static DecisionResponse Noul(double value, string? model = "jev-1.13.0")
        => new(ConformanceProbe.Noul.Definition, [QuestionAnswer.Noul(value)], model);

    public static DecisionResponse Choice(int chosen, double[] probabilities, string? model = "jev-1.13.0")
        => new(ConformanceProbe.Choice.Definition, [QuestionAnswer.Choice(chosen, 0.81, probabilities)], model);

    public static DecisionResponse Score(int level, double value, double[] probabilities, string? model = "jev-1.13.0")
        => new(ConformanceProbe.Score.Definition, [QuestionAnswer.Score(level, value, 0.92, probabilities)], model);

    public static DecisionResponse Mixed(double urgent, int department, double frustration)
        => new(
            ConformanceProbe.Mixed.Definition,
            [QuestionAnswer.Noul(urgent), QuestionAnswer.Choice(department, 0.81, [0.88, 0.12]), QuestionAnswer.Score(1, frustration, 0.92, [0.0, 0.95, 0.05])],
            "jev-1.13.0");

    public const string NoulBody = """{"model":"jev-1.13.0","answers":{"is_urgent":{"type":"noul","noul":0.95}},"usage":{"input_tokens":296,"output_tokens":20}}""";

    public const string ChoiceBody = """{"model":"jev-1.13.0","answers":{"department":{"type":"choice","choice":"billing","probabilities":{"billing":0.88,"technical":0.12},"confidence":0.81}}}""";

    public const string ScoreBody = """{"model":"jev-1.13.0","answers":{"frustration":{"type":"score","score":1.05,"probabilities":{"0":0.0,"1":0.95,"2":0.05},"confidence":0.92}}}""";

    // In alphabetical order, while the probe asks is_urgent, department, frustration.
    public const string MixedBody = """{"model":"jev-1.13.0","answers":{"department":{"type":"choice","choice":"billing","probabilities":{"billing":0.88,"technical":0.12},"confidence":0.81},"frustration":{"type":"score","score":1.05,"probabilities":{"0":0.0,"1":0.95,"2":0.05},"confidence":0.92},"is_urgent":{"type":"noul","noul":0.95}}}""";
}
