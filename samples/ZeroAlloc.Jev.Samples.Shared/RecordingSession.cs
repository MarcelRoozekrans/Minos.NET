using System.Text.Json;

namespace ZeroAlloc.Jev.Samples;

/// <summary>Collects the successful responses of one recording run.</summary>
public sealed class RecordingSession
{
    private readonly SortedDictionary<string, string> _responses = new(StringComparer.Ordinal);
    private readonly Lock _gate = new();

    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _responses.Count;
            }
        }
    }

    public void Add(string requestHash, string responseBody)
    {
        lock (_gate)
        {
            _responses[requestHash] = responseBody;
        }
    }

    /// <summary>
    /// Builds the recordings file; the model is the <c>model</c> field every response reports, or <c>unknown</c> when
    /// none has one.
    /// </summary>
    /// <exception cref="InvalidOperationException">Nothing was recorded, or the responses report different models.</exception>
    public RecordingsFile ToFile(string provider, DateOnly recorded)
    {
        lock (_gate)
        {
            if (_responses.Count == 0)
            {
                throw new InvalidOperationException("Nothing was recorded.");
            }

            var models = _responses.Values.Select(ModelOf).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
            if (models.Count > 1)
            {
                throw new InvalidOperationException(
                    "The responses report different models: " + string.Join(", ", models) + ". Record them again in one run.");
            }

            return new RecordingsFile
            {
                Provider = provider,
                Model = models[0],
                Recorded = recorded.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
                Entries = [.. _responses.Select(r => new RecordedResponse(r.Key, r.Value))],
            };
        }
    }

    private static string ModelOf(string responseBody)
    {
        using var document = JsonDocument.Parse(responseBody);
        return document.RootElement.TryGetProperty("model", out var model) ? model.GetString() ?? "unknown" : "unknown";
    }
}
