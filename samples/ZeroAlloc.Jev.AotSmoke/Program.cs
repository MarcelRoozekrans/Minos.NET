using System.Net;
using System.Text;
using System.Text.Json;

namespace ZeroAlloc.Jev.AotSmoke;

/// <summary>
/// Native AOT smoke test: publishes with PublishAot and exercises the real client and generated code paths over a
/// canned handler. Exits 0 when every check passes, 1 otherwise.
/// </summary>
internal static class Program
{
    private const string NoulResponse = """{"model":"jev-1.13.0","answers":{"is_urgent":{"type":"noul","noul":0.95}},"usage":{"input_tokens":296,"output_tokens":20}}""";
    private const string ModelsResponse = """{"models":[{"name":"jev-latest","description":"The most recent stable, official release.","release_date":"2026-09-15"}]}""";
    private const string ValidationResponse = """{"detail":"questions.is_urgent.instructions is required"}""";
    private const string TriageAnswers = """{"requests_credentials":{"type":"noul","noul":0.1},"team":{"type":"choice","choice":"account","probabilities":{"billing":0.2,"account":0.8},"confidence":0.7},"urgency":{"type":"score","score":1.9,"probabilities":{"0":0.0,"1":0.1,"2":0.9},"confidence":0.8}}""";

    private static int failures;

    private static async Task<int> Main()
    {
        await EvaluateParsesAnswers().ConfigureAwait(false);
        await ValidationErrorCarriesDetail().ConfigureAwait(false);
        await ListModelsReturnsModels().ConfigureAwait(false);
        await MalformedBodyIsInvalidResponse().ConfigureAwait(false);
        await OpenRouterModelListingIsUnsupported().ConfigureAwait(false);
        GeneratedQuestionSetRoundTrips();

        Console.WriteLine(failures == 0 ? "AOT smoke: all checks passed" : "AOT smoke: " + failures + " check(s) failed");
        return failures == 0 ? 0 : 1;
    }

    private static async Task EvaluateParsesAnswers()
    {
        using var http = Http(HttpStatusCode.OK, NoulResponse);
        using var client = new JevClient(http, Options());

        var result = await client.EvaluateAsync(Request()).ConfigureAwait(false);

        Check(result.IsSuccess && result.Value.Answers["is_urgent"] is NoulAnswer { Noul: 0.95 }, "EvaluateAsync parses a Noul answer");
    }

    private static async Task ValidationErrorCarriesDetail()
    {
        using var http = Http(HttpStatusCode.UnprocessableEntity, ValidationResponse);
        using var client = new JevClient(http, Options());

        var result = await client.EvaluateAsync(Request()).ConfigureAwait(false);

        Check(
            result.IsFailure
                && result.Error.Kind == JevErrorKind.Validation
                && result.Error.Detail is { } detail
                && string.Equals(detail.GetProperty("detail").GetString(), "questions.is_urgent.instructions is required", StringComparison.Ordinal),
            "a 422 maps to Validation with its JSON detail");
    }

    private static async Task ListModelsReturnsModels()
    {
        using var http = Http(HttpStatusCode.OK, ModelsResponse);
        using var client = new JevClient(http, Options());

        var result = await client.ListModelsAsync().ConfigureAwait(false);

        Check(
            result.IsSuccess && result.Value.Models.Count == 1 && string.Equals(result.Value.Models[0].Name, "jev-latest", StringComparison.Ordinal),
            "ListModelsAsync parses the models");
    }

    private static async Task MalformedBodyIsInvalidResponse()
    {
        using var http = Http(HttpStatusCode.OK, "not json");
        using var client = new JevClient(http, Options());

        var result = await client.EvaluateAsync(Request()).ConfigureAwait(false);

        Check(result.IsFailure && result.Error.Kind == JevErrorKind.InvalidResponse, "a malformed 2xx body maps to InvalidResponse");
    }

    private static async Task OpenRouterModelListingIsUnsupported()
    {
        using var http = Http(HttpStatusCode.OK, ModelsResponse);
        using var client = new JevClient(http, Options(JevProvider.OpenRouter));

        var result = await client.ListModelsAsync().ConfigureAwait(false);

        Check(result.IsFailure && result.Error.Kind == JevErrorKind.Unsupported, "model listing on OpenRouter is Unsupported");
    }

    private static void GeneratedQuestionSetRoundTrips()
    {
        using var questions = JsonDocument.Parse(SmokeTriage.QuestionsUtf8.ToArray());
        Check(
            string.Equals(
                questions.RootElement.GetProperty("team").GetProperty("criteria").GetProperty("account").GetString(),
                "Login, profile, permissions",
                StringComparison.Ordinal),
            "QuestionsUtf8 carries the Choice criteria");

        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(TriageAnswers));
        reader.Read();
        var triage = SmokeTriage.Parse(ref reader);
        Check(
            !triage.RequestsCredentials.Value && triage.Team.Value == Team.Account && triage.Urgency.Value == Urgency.High,
            "the generated Parse reads typed answers");
    }

    private static HttpClient Http(HttpStatusCode status, string body)
        => new(new CannedHandler(status, body)) { BaseAddress = new Uri("https://example.test/api/") };

    private static JevClientOptions Options(JevProvider provider = JevProvider.TypeSafe)
        => new() { ApiKey = "smoke-key", Provider = provider };

    private static SystemOneRequest Request() => new()
    {
        State = "Help! My payouts have been failing for 3 days.",
        Questions = new Dictionary<string, JevQuestion>(StringComparer.Ordinal)
        {
            ["is_urgent"] = new NoulQuestion { Instructions = "Does this convey urgency?" },
        },
    };

    private static void Check(bool passed, string description)
    {
        Console.WriteLine((passed ? "PASS " : "FAIL ") + description);
        if (!passed)
        {
            failures++;
        }
    }
}
