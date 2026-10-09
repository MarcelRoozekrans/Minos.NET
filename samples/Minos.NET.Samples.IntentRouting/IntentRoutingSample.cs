using System.Globalization;
using System.Text;

namespace Minos.Samples.IntentRouting;

public sealed record RoutedRequest(string Id, string Text, TravelIntent Intent, ConfidenceTier Tier, RequestHandler Handler, bool Urgent);

public sealed record RoutingReport(IReadOnlyList<RoutedRequest> Requests)
{
    /// <summary>How many requests were settled without calling a language model.</summary>
    public int WithoutLanguageModel => Requests.Count(r => r.Handler != RequestHandler.AssistantModel);

    public string Render()
    {
        var text = new StringBuilder();
        foreach (var r in Requests)
        {
            var line = string.Create(CultureInfo.InvariantCulture, $"{r.Id}  {r.Intent,-13} {r.Tier,-7} {r.Handler,-14} {(r.Urgent ? "URGENT" : string.Empty)}");
            text.Append(line.TrimEnd()).Append('\n');
        }

        text.Append(CultureInfo.InvariantCulture, $"{WithoutLanguageModel} of {Requests.Count} requests needed no language model\n");
        return text.ToString();
    }
}

/// <summary>Classifies each request with one Jev call and routes it: code, an assistant model or a person.</summary>
public static class IntentRoutingSample
{
    public static async Task<RoutingReport> RunAsync(IDecisionClient jev, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(jev);
        var routed = new List<RoutedRequest>();
        foreach (var (id, text) in Requests.All)
        {
            var result = await jev.EvaluateAsync<TravelRequest>(text, cancellationToken).ConfigureAwait(false);
            if (result.IsFailure)
            {
                throw new InvalidOperationException(id + ": " + result.Error.Kind + ": " + result.Error.Message);
            }

            var (handler, tier, urgent) = TravelRouting.Route(result.Value);
            routed.Add(new RoutedRequest(id, text, result.Value.Intent.Value, tier, handler, urgent));
        }

        return new RoutingReport(routed);
    }
}
