using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Minos.Samples.ProviderSwitch;

namespace Minos.Samples.Tests;

/// <summary>
/// Stands in for a provider: answers each ticket with its canned team and confidence, found by the ticket's text in the
/// request's state, and keeps the hash of every request body.
/// </summary>
internal sealed class CannedTickets(string model) : HttpMessageHandler
{
    private readonly List<string> _hashes = [];

    /// <summary>The team and confidence of each ticket, by its id.</summary>
    public static IReadOnlyDictionary<string, (string Team, double Confidence)> Answers { get; } =
        new Dictionary<string, (string Team, double Confidence)>(StringComparer.Ordinal)
        {
            ["s01"] = ("billing", 0.97),
            ["s02"] = ("technical", 0.95),
            ["s03"] = ("account", 0.99),
            ["s04"] = ("sales", 0.93),
            ["s05"] = ("technical", 0.88),
            ["s06"] = ("account", 0.91),
            ["s07"] = ("sales", 0.62),
            ["s08"] = ("billing", 0.84),
        };

    public IReadOnlyList<string> Hashes => _hashes;

    public int Requests => _hashes.Count;

    /// <summary>A System One response that chooses <paramref name="team"/> with <paramref name="confidence"/>.</summary>
    public static string Body(string model, string team, double confidence)
        => string.Create(
            CultureInfo.InvariantCulture,
            $$$"""{"model":"{{{model}}}","answers":{"team":{"type":"choice","choice":"{{{team}}}","probabilities":{{{Probabilities(team)}}},"confidence":{{{confidence}}}}},"usage":{"input_tokens":1,"output_tokens":1}}""");

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = await request.Content!.ReadAsByteArrayAsync(cancellationToken);
        _hashes.Add(RequestHash.Of(body));
        using var json = JsonDocument.Parse(body);
        var state = json.RootElement.GetProperty("state").GetString();
        var id = Tickets.All.First(t => string.Equals(t.Text, state, StringComparison.Ordinal)).Id;
        var (team, confidence) = Answers[id];
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(Body(model, team, confidence), Encoding.UTF8, "application/json"),
        };
    }

    // The team keys come from the question itself, so the canned options cannot drift from the sample's.
    private static string Probabilities(string team)
        => "{" + string.Join(
            ",",
            TicketQuestions.Definition.Questions[0].Options.Select(
                option => "\"" + option.Key + "\":" + (string.Equals(option.Key, team, StringComparison.Ordinal) ? "1" : "0"))) + "}";
}
