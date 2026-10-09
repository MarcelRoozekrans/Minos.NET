using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Minos.Tests;

/// <summary>
/// Listens to the <c>Minos</c> source and meter for one test, and to the <c>ZeroAlloc.Rest</c> source when asked.
/// It keeps every stopped span, the tags each Minos span carried when the sampler saw it, and every measurement.
/// </summary>
/// <remarks>Uses the source name as a literal, not <c>DecisionTelemetry.SourceName</c>, so the integration tests can link it.</remarks>
internal sealed class TelemetryCapture : IDisposable
{
    private const string DecisionSource = "Minos";
    private const string RestSource = "ZeroAlloc.Rest";

    private readonly Lock _gate = new();
    private readonly List<Activity> _spans = [];
    private readonly List<KeyValuePair<string, object?>[]> _starts = [];
    private readonly List<CapturedPoint> _points = [];
    private readonly Dictionary<string, Instrument> _instruments = new(StringComparer.Ordinal);
    private readonly ActivityListener _activities;
    private readonly MeterListener _meters;

    public TelemetryCapture(bool traces = true, bool metrics = true, bool rest = false)
    {
        _activities = new ActivityListener
        {
            ShouldListenTo = source => source.Name is DecisionSource || (rest && source.Name is RestSource),
            Sample = Sample,
            ActivityStopped = Stopped,
        };
        if (traces)
        {
            ActivitySource.AddActivityListener(_activities);
        }

        _meters = new MeterListener { InstrumentPublished = Published };
        _meters.SetMeasurementEventCallback<double>((instrument, value, tags, _) => Measured(instrument, value, tags));
        _meters.SetMeasurementEventCallback<long>((instrument, value, tags, _) => Measured(instrument, value, tags));
        if (metrics)
        {
            _meters.Start();
        }
    }

    /// <summary>Gets every measurement so far.</summary>
    public CapturedPoint[] AllPoints
    {
        get
        {
            lock (_gate)
            {
                return [.. _points];
            }
        }
    }

    /// <summary>Gets the stopped spans from <paramref name="source"/>.</summary>
    public Activity[] Spans(string source)
    {
        lock (_gate)
        {
            return [.. _spans.Where(span => string.Equals(span.Source.Name, source, StringComparison.Ordinal))];
        }
    }

    /// <summary>Gets the one stopped Minos span; throws when there is not exactly one.</summary>
    public Activity Span()
    {
        var spans = Spans(DecisionSource);
        return spans.Length == 1 ? spans[0] : throw new InvalidOperationException($"Expected one Minos span, found {spans.Length}.");
    }

    /// <summary>Gets the tags the one Minos span carried when the sampler saw it.</summary>
    public KeyValuePair<string, object?>[] StartTags()
    {
        lock (_gate)
        {
            return _starts.Count == 1 ? _starts[0] : throw new InvalidOperationException($"Expected one Minos span start, found {_starts.Count}.");
        }
    }

    /// <summary>Gets the measurements of <paramref name="metric"/>.</summary>
    public CapturedPoint[] Points(string metric) => [.. AllPoints.Where(point => string.Equals(point.Metric, metric, StringComparison.Ordinal))];

    /// <summary>
    /// Gets the one measurement, of <paramref name="metric"/> or of any metric; throws when there is not exactly one.
    /// Used instead of xUnit's <c>Assert.Single</c>, which HLQ005 reports.
    /// </summary>
    public CapturedPoint OnlyPoint(string? metric = null)
    {
        var points = metric is null ? AllPoints : Points(metric);
        return points.Length == 1 ? points[0] : throw new InvalidOperationException($"Expected one point of {metric ?? "any metric"}, found {points.Length}.");
    }

    /// <summary>Gets the instrument named <paramref name="metric"/>, once it has been published.</summary>
    public Instrument Instrument(string metric)
    {
        lock (_gate)
        {
            return _instruments[metric];
        }
    }

    public void Dispose()
    {
        _meters.Dispose();
        _activities.Dispose();
    }

    private ActivitySamplingResult Sample(ref ActivityCreationOptions<ActivityContext> options)
    {
        if (options.Source.Name is DecisionSource)
        {
            var tags = options.Tags is { } given ? given.ToArray() : [];
            lock (_gate)
            {
                _starts.Add(tags);
            }
        }

        return ActivitySamplingResult.AllDataAndRecorded;
    }

    private void Stopped(Activity activity)
    {
        lock (_gate)
        {
            _spans.Add(activity);
        }
    }

    private void Published(Instrument instrument, MeterListener listener)
    {
        if (instrument.Meter.Name is not DecisionSource)
        {
            return;
        }

        lock (_gate)
        {
            _instruments[instrument.Name] = instrument;
        }

        listener.EnableMeasurementEvents(instrument);
    }

    private void Measured(Instrument instrument, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        var point = new CapturedPoint(instrument.Name, instrument.Unit, value, tags.ToArray());
        lock (_gate)
        {
            _points.Add(point);
        }
    }
}

/// <summary>One measurement: the metric, its unit, the value and its tags.</summary>
internal sealed record CapturedPoint(string Metric, string? Unit, double Value, KeyValuePair<string, object?>[] Tags)
{
    /// <summary>Gets the tag's value, or <see langword="null"/> when the point has no such tag.</summary>
    public object? Tag(string key) => Tags.Tag(key);

    /// <summary>Gets the tag names, sorted, for comparing tag sets.</summary>
    public string[] TagNames => [.. Tags.Select(tag => tag.Key).Order(StringComparer.Ordinal)];
}

/// <summary>Reads captured tags. Not named <c>Telemetry</c>, which would shadow the <c>Minos.Telemetry</c> namespace.</summary>
internal static class CapturedTags
{
    /// <summary>Gets the value of tag <paramref name="key"/>, or <see langword="null"/> when absent.</summary>
    public static object? Tag(this KeyValuePair<string, object?>[] tags, string key)
    {
        foreach (var tag in tags)
        {
            if (string.Equals(tag.Key, key, StringComparison.Ordinal))
            {
                return tag.Value;
            }
        }

        return null;
    }
}
