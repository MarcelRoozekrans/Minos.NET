using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Time.Testing;
using static Minos.Recording.Tests.Stage;

namespace Minos.Recording.Tests;

/// <summary>
/// The privacy gate: the state, the decision id and the erasure key, and a provider's error message, never reach a log
/// message or its state, a span's tags or events, a metric's tags, or the message or text of an exception the app sees.
/// </summary>
[Collection(RecordingListeners.Name)]
public sealed class NeverRecordedTests
{
    private const string StateSentinel = "SENTINEL-7f3a";
    private const string IdSentinel = "ID-SENTINEL";
    private const string KeySentinel = "KEY-SENTINEL";

    private static readonly string[] Sentinels = [StateSentinel, IdSentinel, KeySentinel];

    private static DecisionRequest SentinelRequest() => Request($"My card has not arrived. {StateSentinel}");

    public static TheoryData<string> Scenarios => new()
    {
        "success",
        "inner error",
        "throwing sink",
        "throwing redactor",
        "full queue",
        "erasure failure",
        "flush timeout",
    };

    [Theory]
    [MemberData(nameof(Scenarios))]
    public async Task NoSentinel_ReachesALog_ASpan_AMetricTag_OrAnException(string scenario)
    {
        using var capture = new Capture();
        var logs = new FakeLogCollector();
        await RunAsync(scenario, logs, capture);

        foreach (var record in logs.GetSnapshot())
        {
            capture.Add(record.Message);
            foreach (var pair in record.StructuredState ?? [])
            {
                capture.Add(pair.Key);
                capture.Add(pair.Value);
            }

            capture.Add(record.Exception?.ToString());
        }

        Assert.NotEmpty(capture.Texts);
        foreach (var text in capture.Texts)
        {
            foreach (var sentinel in Sentinels)
            {
                Assert.DoesNotContain(sentinel, text, StringComparison.Ordinal);
            }
        }
    }

    private static async Task RunAsync(string scenario, FakeLogCollector logs, Capture capture)
    {
        using var factory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Debug).AddProvider(new FakeLoggerProvider(logs)));
        switch (scenario)
        {
            case "success":
            {
                var sink = new RecordingSpySink();
                await using (var recorder = new DecisionRecorder(sink, null, factory))
                {
                    using var client = Pipeline(new ScriptedClient(Metadata), recorder);
                    using (DecisionRecording.Begin(IdSentinel, KeySentinel))
                    {
                        Assert.True((await client.EvaluateAsync(SentinelRequest())).IsSuccess);
                        capture.Add(await Assert.ThrowsAsync<InvalidOperationException>(() => client.EvaluateAsync(SentinelRequest()).AsTask()));
                        capture.Add(Assert.Throws<InvalidOperationException>(() => DecisionRecording.Begin(IdSentinel, KeySentinel)));
                    }

                    await recorder.AddOutcomeAsync(IdSentinel, TestRecords.Signal());
                    await recorder.EraseDecisionAsync(IdSentinel).AsTask().WaitAsync(Bound);
                    await recorder.EraseAsync(KeySentinel).AsTask().WaitAsync(Bound);
                }

                Assert.Equal(4, sink.Calls.Count);
                break;
            }

            case "inner error":
            {
                var sink = new RecordingSpySink();
                await using (var recorder = new DecisionRecorder(sink, null, factory))
                {
                    var inner = new ScriptedClient(Metadata).ThenFail(DecisionErrorKind.Server, $"The upstream quoted {StateSentinel}, {IdSentinel} and {KeySentinel}.");
                    using var client = Pipeline(inner, recorder);
                    using (DecisionRecording.Begin(IdSentinel, KeySentinel))
                    {
                        Assert.False((await client.EvaluateAsync(SentinelRequest())).IsSuccess);
                    }

                    await recorder.FlushAsync().AsTask().WaitAsync(Bound);
                }

                Assert.Single(sink.Calls);
                break;
            }

            case "throwing sink":
            {
                // The sink's exception quotes the decision id; only its type name may be logged.
                var sink = new ThrowingSink(_ => true);
                await using (var recorder = new DecisionRecorder(sink, null, factory))
                {
                    using var client = Pipeline(new ScriptedClient(Metadata), recorder);
                    using (DecisionRecording.Begin(IdSentinel, KeySentinel))
                    {
                        Assert.True((await client.EvaluateAsync(SentinelRequest())).IsSuccess);
                    }

                    await recorder.AddOutcomeAsync(IdSentinel, TestRecords.Signal());
                    await recorder.FlushAsync().AsTask().WaitAsync(Bound);
                }

                Assert.Equal(2, sink.Thrown.Count);
                break;
            }

            case "throwing redactor":
            {
                var sink = new RecordingSpySink();
                var options = new RecordingOptions
                {
                    Redact = state => throw new FormatException($"Cannot redact {state} for {IdSentinel} and {KeySentinel}."),
                };
                await using (var recorder = new DecisionRecorder(sink, options, factory))
                {
                    using var client = Pipeline(new ScriptedClient(Metadata), recorder);
                    using (DecisionRecording.Begin(IdSentinel, KeySentinel))
                    {
                        Assert.True((await client.EvaluateAsync(SentinelRequest())).IsSuccess);
                    }

                    await recorder.FlushAsync().AsTask().WaitAsync(Bound);
                }

                Assert.Empty(sink.Calls);
                break;
            }

            case "full queue":
            {
                var sink = new BlockingSink();
                await using (var recorder = new DecisionRecorder(sink, new RecordingOptions { Capacity = 1 }, factory))
                {
                    using var client = Pipeline(new ScriptedClient(Metadata), recorder);
                    for (var i = 0; i < 3; i++)
                    {
                        using (DecisionRecording.Begin($"{IdSentinel}-{i}", KeySentinel))
                        {
                            Assert.True((await client.EvaluateAsync(SentinelRequest())).IsSuccess);
                        }

                        if (i == 0)
                        {
                            await sink.WaitEnteredAsync();
                        }
                    }

                    sink.Release();
                    await recorder.FlushAsync().AsTask().WaitAsync(Bound);
                }

                Assert.Equal(2, sink.Spy.Calls.Count);
                Assert.Contains(logs.GetSnapshot(), record => record.Id.Id == 1009);
                break;
            }

            case "erasure failure":
            {
                // The sink's own message carries no sentinel, so the whole text of the exception the app sees is checked.
                var sink = new FailingSink();
                await using (var recorder = new DecisionRecorder(sink, null, factory))
                {
                    capture.Add(await Assert.ThrowsAsync<RecordingException>(() => recorder.EraseAsync(KeySentinel).AsTask().WaitAsync(Bound)));
                    capture.Add(await Assert.ThrowsAsync<RecordingException>(() => recorder.EraseDecisionAsync(IdSentinel).AsTask().WaitAsync(Bound)));
                }

                Assert.Contains(logs.GetSnapshot(), record => record.Id.Id == 1010);
                break;
            }

            default:
            {
                // A flush timeout: a record, an outcome and both erasures are lost.
                var time = new FakeTimeProvider();
                var sink = new BlockingSink();
                var recorder = new DecisionRecorder(sink, new RecordingOptions { FlushTimeout = TimeSpan.FromMilliseconds(100) }, factory, time);
                using var client = Pipeline(new ScriptedClient(Metadata), recorder);
                using (DecisionRecording.Begin(IdSentinel, KeySentinel))
                {
                    Assert.True((await client.EvaluateAsync(SentinelRequest())).IsSuccess);
                }

                await sink.WaitEnteredAsync();
                using (DecisionRecording.Begin($"{IdSentinel}-2", KeySentinel))
                {
                    Assert.True((await client.EvaluateAsync(SentinelRequest())).IsSuccess);
                }

                await recorder.AddOutcomeAsync(IdSentinel, TestRecords.Signal());
                var byKey = recorder.EraseAsync(KeySentinel).AsTask();
                var byId = recorder.EraseDecisionAsync(IdSentinel).AsTask();
                var dispose = recorder.DisposeAsync().AsTask();
                time.Advance(TimeSpan.FromMilliseconds(100));
                await dispose.WaitAsync(Bound);
                capture.Add(await Assert.ThrowsAsync<RecordingException>(() => byKey.WaitAsync(Bound)));
                capture.Add(await Assert.ThrowsAsync<RecordingException>(() => byId.WaitAsync(Bound)));
                await recorder.Writer.WaitAsync(Bound);
                Assert.Contains(logs.GetSnapshot(), record => record.Id.Id == 1012);
                break;
            }
        }
    }

    // The core's telemetry stage sits inside, so its spans and metrics are captured too.
    private static IDecisionClient Pipeline(ScriptedClient inner, DecisionRecorder recorder)
        => inner.AsBuilder().UseRecording(recorder).UseOpenTelemetry().Build();

    /// <summary>A sink that fails every call with a message that quotes nothing.</summary>
    private sealed class FailingSink : IDecisionRecordSink
    {
        public ValueTask WriteAsync(DecisionRecord record, CancellationToken cancellationToken) => Fail();

        public ValueTask AddOutcomeAsync(string decisionId, DecisionOutcome outcome, CancellationToken cancellationToken) => Fail();

        public ValueTask EraseAsync(string erasureKey, CancellationToken cancellationToken) => Fail();

        public ValueTask EraseDecisionAsync(string decisionId, CancellationToken cancellationToken) => Fail();

        private static ValueTask Fail() => ValueTask.FromException(new IOException("The store is down."));
    }

    /// <summary>Collects every span name, status, tag and event, and every metric tag, on the "Minos" sources.</summary>
    private sealed class Capture : IDisposable
    {
        private readonly List<string> _texts = [];
        private readonly ActivityListener _activities;
        private readonly MeterListener _meters = new();

        public Capture()
        {
            _activities = new ActivityListener
            {
                ShouldListenTo = source => string.Equals(source.Name, "Minos", StringComparison.Ordinal),
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
                ActivityStopped = Add,
            };
            ActivitySource.AddActivityListener(_activities);
            _meters.InstrumentPublished = (instrument, listener) =>
            {
                if (string.Equals(instrument.Meter.Name, "Minos", StringComparison.Ordinal))
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            };
            _meters.SetMeasurementEventCallback<long>((instrument, _, tags, _) => Add(instrument, tags));
            _meters.SetMeasurementEventCallback<int>((instrument, _, tags, _) => Add(instrument, tags));
            _meters.SetMeasurementEventCallback<double>((instrument, _, tags, _) => Add(instrument, tags));
            _meters.Start();
        }

        public IReadOnlyList<string> Texts
        {
            get
            {
                lock (_texts)
                {
                    return [.. _texts];
                }
            }
        }

        public void Add(string? text)
        {
            if (text is not null)
            {
                lock (_texts)
                {
                    _texts.Add(text);
                }
            }
        }

        public void Add(Exception exception)
        {
            Add(exception.Message);
            Add(exception.ToString());
        }

        public void Dispose()
        {
            _activities.Dispose();
            _meters.Dispose();
        }

        private void Add(Activity activity)
        {
            Add(activity.OperationName);
            Add(activity.DisplayName);
            Add(activity.StatusDescription);
            AddTags(activity.TagObjects);
            foreach (var item in activity.Events)
            {
                Add(item.Name);
                AddTags(item.Tags);
            }
        }

        private void Add(Instrument instrument, ReadOnlySpan<KeyValuePair<string, object?>> tags)
        {
            Add(instrument.Name);
            foreach (var tag in tags)
            {
                Add(tag.Key);
                Add(Convert.ToString(tag.Value, CultureInfo.InvariantCulture));
            }
        }

        private void AddTags(IEnumerable<KeyValuePair<string, object?>> tags)
        {
            foreach (var tag in tags)
            {
                Add(tag.Key);
                Add(Convert.ToString(tag.Value, CultureInfo.InvariantCulture));
            }
        }
    }
}
