using System.Globalization;

namespace ZeroAlloc.Jev.Benchmarks.Compare.Adapters;

/// <summary>Every answer of one workload call, as a client read it, for checking against <see cref="Workload.Expected"/>.</summary>
/// <param name="Model">The model the response names, or <see langword="null"/> when the client's typed API does not expose it.</param>
/// <param name="Choice">The chosen <c>intent</c> option.</param>
/// <param name="Confidence">The <c>intent</c> answer's confidence.</param>
/// <param name="Probabilities">The <c>intent</c> answer's probability per option.</param>
/// <param name="Noul">The <c>travels_within24_hours</c> probability.</param>
public sealed record WorkloadAnswers(
    string? Model,
    string Choice,
    double Confidence,
    IReadOnlyDictionary<string, double> Probabilities,
    double Noul)
{
    /// <summary>Throws unless these answers equal <paramref name="expected"/>.</summary>
    /// <param name="client">The client the answers came from, for the message.</param>
    /// <param name="expected">The answers the mock serves.</param>
    /// <exception cref="InvalidOperationException">An answer differs.</exception>
    public void EnsureEquals(string client, WorkloadAnswers expected)
    {
        ArgumentNullException.ThrowIfNull(expected);
        var mismatches = new List<string>();
        if (Model is not null && !string.Equals(Model, expected.Model, StringComparison.Ordinal))
        {
            mismatches.Add("model " + Model);
        }

        if (!string.Equals(Choice, expected.Choice, StringComparison.Ordinal))
        {
            mismatches.Add("intent.choice " + Choice);
        }

        if (Confidence != expected.Confidence)
        {
            mismatches.Add("intent.confidence " + Confidence.ToString(CultureInfo.InvariantCulture));
        }

        if (Probabilities.Count != expected.Probabilities.Count)
        {
            mismatches.Add("intent.probabilities count " + Probabilities.Count.ToString(CultureInfo.InvariantCulture));
        }

        foreach (var (option, probability) in expected.Probabilities)
        {
            if (!Probabilities.TryGetValue(option, out var actual) || actual != probability)
            {
                mismatches.Add("intent.probabilities." + option);
            }
        }

        if (Noul != expected.Noul)
        {
            mismatches.Add("travels_within24_hours.noul " + Noul.ToString(CultureInfo.InvariantCulture));
        }

        if (mismatches.Count > 0)
        {
            throw new InvalidOperationException(client + " read unexpected answers: " + string.Join(", ", mismatches));
        }
    }
}
