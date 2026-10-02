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

    /// <summary>Builds the recordings file; the model is read from the first response's <c>model</c> field.</summary>
    /// <exception cref="InvalidOperationException">Nothing was recorded.</exception>
    public RecordingsFile ToFile(string provider, DateOnly recorded)
    {
        lock (_gate)
        {
            if (_responses.Count == 0)
            {
                throw new InvalidOperationException("Nothing was recorded.");
            }

            using var first = JsonDocument.Parse(_responses.Values.First());
            var model = first.RootElement.TryGetProperty("model", out var m) ? m.GetString() ?? "unknown" : "unknown";
            return new RecordingsFile
            {
                Provider = provider,
                Model = model,
                Recorded = recorded.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
                Entries = [.. _responses.Select(r => new RecordedResponse(r.Key, r.Value))],
            };
        }
    }
}
