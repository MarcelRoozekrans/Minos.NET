using System.Globalization;
using System.Text;

namespace Minos.Samples.Guardrails;

public sealed record ScreenedMessage(string Id, string Text, GuardrailDecision Strict, GuardrailDecision Lenient);

public sealed record GuardrailsReport(IReadOnlyList<ScreenedMessage> Messages)
{
    public string Render()
    {
        var text = new StringBuilder();
        foreach (var m in Messages)
        {
            text.Append(CultureInfo.InvariantCulture, $"{m.Id}  strict: {m.Strict.Action,-6} ({m.Strict.Reason})  lenient: {m.Lenient.Action,-6} ({m.Lenient.Reason})\n");
        }

        return text.ToString();
    }
}

/// <summary>Screens each message once and decides it under both policies: one request per message, no second call.</summary>
public static class GuardrailsSample
{
    public static async Task<GuardrailsReport> RunAsync(IDecisionClient client, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);
        var results = new List<ScreenedMessage>();
        foreach (var (id, text) in Messages.All)
        {
            var result = await client.EvaluateAsync<MessageScreen>(text, cancellationToken).ConfigureAwait(false);
            if (result.IsFailure)
            {
                throw new InvalidOperationException(id + ": " + result.Error.Kind + ": " + result.Error.Message);
            }

            results.Add(new ScreenedMessage(id, text, GuardrailPolicy.Strict.Decide(result.Value), GuardrailPolicy.Lenient.Decide(result.Value)));
        }

        return new GuardrailsReport(results);
    }
}
