using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Net;
using System.Text;

namespace Minos.AotSmoke;

/// <summary>The client's spans and metrics under Native AOT, through real listeners.</summary>
internal static class TelemetryChecks
{
    public static async Task RetriedEvaluationIsOneSpanOverTwoAttempts()
    {
        var spans = new List<Activity>();
        var startTags = new List<KeyValuePair<string, object?>>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = static source => source.Name is "Minos" or "ZeroAlloc.Rest",
            Sample = (ref ActivityCreationOptions<ActivityContext> options) =>
            {
                if (options.Source.Name is "Minos" && options.Tags is { } tags)
                {
                    startTags.AddRange(tags);
                }

                return ActivitySamplingResult.AllDataAndRecorded;
            },
            ActivityStopped = spans.Add,
        };
        ActivitySource.AddActivityListener(listener);
        var handler = new SequenceHandler(Program.NoulResponse, HttpStatusCode.ServiceUnavailable, HttpStatusCode.OK);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.test/api/") };
        using var client = new DecisionClient(http, new DecisionClientOptions { ApiKey = "smoke-key", InitialBackoff = TimeSpan.FromMilliseconds(10) });

        var result = await client.EvaluateAsync(Program.Request()).ConfigureAwait(false);

        var jev = spans.Where(span => span.Source.Name is "Minos").ToArray();
        var rest = spans.Where(span => span.Source.Name is "ZeroAlloc.Rest").ToArray();
        Program.Check(
            result.IsSuccess
                && jev.Length == 1
                && jev[0].Kind == ActivityKind.Client
                && jev[0].DisplayName is "evaluate jev-latest"
                && jev[0].Status == ActivityStatusCode.Unset
                && rest.Length == 2
                && rest.All(attempt => attempt.ParentSpanId == jev[0].SpanId),
            "a retried evaluation is one Jev client span over two ZeroAlloc.Rest attempt spans under Native AOT");
        Program.Check(
            startTags.Exists(tag => tag.Key is "server.address" && tag.Value is "example.test")
                && startTags.Exists(tag => tag.Key is "server.port" && tag.Value is 443)
                && startTags.Exists(tag => tag.Key is "gen_ai.request.model" && tag.Value is "jev-latest")
                && startTags.Exists(tag => tag.Key is "jev.request.question_count" && tag.Value is 1),
            "the span's tags reach the sampler at start under Native AOT");
    }

    public static async Task TypedEvaluationRecordsItsMetrics()
    {
        var points = new List<(string Name, string? Unit, double Value)>();
        IReadOnlyList<double>? durationBuckets = null;
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, meters) =>
            {
                if (instrument.Meter.Name is "Minos")
                {
                    if (instrument.Name is "gen_ai.client.operation.duration" && instrument is Histogram<double> histogram)
                    {
                        durationBuckets = histogram.Advice?.HistogramBucketBoundaries;
                    }

                    meters.EnableMeasurementEvents(instrument);
                }
            },
        };
        listener.SetMeasurementEventCallback<double>((instrument, value, _, _) => points.Add((instrument.Name, instrument.Unit, value)));
        listener.SetMeasurementEventCallback<long>((instrument, value, _, _) => points.Add((instrument.Name, instrument.Unit, value)));
        listener.Start();

        // The handler waits 50 ms, so the duration has a known minimum: 0.05 in seconds, 50 or more in milliseconds. The
        // check accepts 0.04 and up, leaving 10 ms of slack for timer and stopwatch granularity, which still rules out
        // milliseconds.
        using var http = new HttpClient(new DelayedHandler(TimeSpan.FromMilliseconds(50), Program.TriageResponse)) { BaseAddress = new Uri("https://example.test/api/") };
        using var client = new DecisionClient(http, new DecisionClientOptions { ApiKey = "smoke-key" });

        var result = await client.EvaluateAsync<SmokeTriage>(SmokeAnswers.State).ConfigureAwait(false);

        var confidences = points.Where(point => point.Name is "jev.answer.confidence").Select(point => point.Value).ToArray();
        var duration = points.Where(point => point.Name is "gen_ai.client.operation.duration").ToArray();
        Program.Check(
            result.IsSuccess
                && confidences.SequenceEqual([0.7, 0.8])
                && duration.Length == 1 && duration[0].Unit is "s" && duration[0].Value >= 0.04 && duration[0].Value < 30
                && durationBuckets?.Count == 14
                && points.Exists(point => point.Name is "gen_ai.client.inference.usage.input_tokens" && point.Value == 296)
                && points.Exists(point => point.Name is "gen_ai.client.inference.operation.output_tokens" && point.Value == 20),
            "a typed evaluation records its duration in seconds, its tokens and one confidence per Choice and Score under Native AOT");
    }

    public static async Task FailedEvaluationIsAnError()
    {
        var spans = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = static source => source.Name is "Minos",
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = spans.Add,
        };
        ActivitySource.AddActivityListener(listener);
        using var http = new HttpClient(new CannedHandler(HttpStatusCode.UnprocessableEntity, Program.ValidationResponse)) { BaseAddress = new Uri("https://example.test/api/") };
        using var client = new DecisionClient(http, new DecisionClientOptions { ApiKey = "smoke-key" });

        var result = await client.EvaluateAsync(Program.Request()).ConfigureAwait(false);

        Program.Check(
            result.IsFailure
                && spans.Count == 1
                && spans[0].Status == ActivityStatusCode.Error
                && spans[0].StatusDescription is null
                && spans[0].GetTagItem("error.type") is "Validation",
            "a failed evaluation is an Error span with its error.type and no description under Native AOT");
    }

    [Covers("Minos.DecisionClient.EvaluateAsync(Minos.SystemOneRequest! request, System.Threading.CancellationToken cancellationToken) -> System.Threading.Tasks.ValueTask<ZeroAlloc.Results.Result<Minos.SystemOneResponse!, Minos.DecisionError!>>")]
    public static async Task CancelledEvaluationSetsErrorTypeWithoutItsMessage()
    {
        var spans = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = static source => source.Name is "Minos",
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = spans.Add,
        };
        ActivitySource.AddActivityListener(listener);
        using var http = new HttpClient(new DelayedHandler(TimeSpan.FromSeconds(30), Program.NoulResponse)) { BaseAddress = new Uri("https://example.test/api/") };
        using var client = new DecisionClient(http, new DecisionClientOptions { ApiKey = "smoke-key", MaxRetries = 0 });
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        Exception? raised = null;

        try
        {
            await client.EvaluateAsync(Program.Request(), cancel.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException exception)
        {
            raised = exception;
        }

        Program.Check(
            raised is not null
                && spans.Count == 1
                && spans[0].Status == ActivityStatusCode.Error
                && string.IsNullOrEmpty(spans[0].StatusDescription)
                && spans[0].GetTagItem("error.type") is string errorType
                && string.Equals(errorType, raised.GetType().FullName, StringComparison.Ordinal),
            "a cancelled evaluation is an Error span with the exception's full type name as error.type and no description under Native AOT");
    }

    /// <summary>Answers with one canned 200 response after a fixed delay, so a call's duration has a known minimum.</summary>
    private sealed class DelayedHandler(TimeSpan delay, string body) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
                RequestMessage = request,
            };
        }
    }
}
