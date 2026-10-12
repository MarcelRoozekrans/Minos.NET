using System.Globalization;
using System.Text;

namespace Minos.Samples.ProviderSwitch;

/// <summary>One ticket and the team the model chose for it.</summary>
public sealed record RoutedTicket(string Id, string Text, string Team, double Confidence);

/// <summary>The routed tickets, the provider that was asked, and the model, or models, that answered.</summary>
public sealed record ProviderSwitchReport(string Provider, string Model, IReadOnlyList<RoutedTicket> Tickets)
{
    public string Render()
    {
        var text = new StringBuilder();
        foreach (var t in Tickets)
        {
            text.Append(CultureInfo.InvariantCulture, $"{t.Id}  {t.Team,-10} {t.Confidence:0.00}").Append('\n');
        }

        text.Append(CultureInfo.InvariantCulture, $"Answered by {Provider} with {Model}\n");
        return text.ToString();
    }
}

/// <summary>Routes every ticket with one request each, through whichever provider the client was configured for.</summary>
public static class ProviderSwitchSample
{
    public static async Task<ProviderSwitchReport> RunAsync(IDecisionClient client, string provider, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentException.ThrowIfNullOrEmpty(provider);
        var options = TicketQuestions.Definition.Questions[0].Options;
        var routed = new List<RoutedTicket>();
        var models = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var (id, text) in Tickets.All)
        {
            // A DecisionRequest rather than a typed call, so the response's model can be read as well as the answer.
            var result = await client.EvaluateAsync(new DecisionRequest(TicketQuestions.Definition, text), cancellationToken).ConfigureAwait(false);
            if (result.IsFailure)
            {
                throw new InvalidOperationException(id + ": " + result.Error.Kind + ": " + result.Error.Message);
            }

            var answer = result.Value.Answers[0];
            routed.Add(new RoutedTicket(id, text, options[answer.ChosenIndex].Key, answer.Confidence));
            models.Add(result.Value.Model ?? "an unnamed model");
        }

        return new ProviderSwitchReport(provider, string.Join(", ", models), routed);
    }
}
