using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;

namespace Minos.Caching.Tests;

public sealed class CacheTelemetryTests
{
    private const string Model = "cache-telemetry-test";

    [Fact]
    public async Task A_hit_is_tagged_on_the_span_and_logged_as_cached()
    {
        using var services = Responses.Services();
        var spans = new List<Activity>();
        // A model no other test uses: test classes run in parallel, and the listener hears every Minos span in the
        // process, so the callback keeps only this test's spans, under the lock.
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => string.Equals(source.Name, "Minos", StringComparison.Ordinal),
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = span =>
            {
                if (string.Equals(span.GetTagItem("gen_ai.request.model") as string, Model, StringComparison.Ordinal))
                {
                    lock (spans)
                    {
                        spans.Add(span);
                    }
                }
            },
        };
        ActivitySource.AddActivityListener(listener);
        var collector = new FakeLogCollector();
        using var loggerFactory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Debug).AddProvider(new FakeLoggerProvider(collector)));
        using var client = new ScriptedClient().AsBuilder()
            .UseOpenTelemetry()
            .UseLogging(loggerFactory)
            .UseCaching()
            .Build(services);

        var request = Responses.Request() with { Model = Model };
        await client.EvaluateAsync(request);
        await client.EvaluateAsync(request);

        Activity[] ours;
        lock (spans)
        {
            ours = [.. spans];
        }

        Assert.Equal(2, ours.Length);
        Assert.Null(ours[0].GetTagItem("minos.cache.hit"));
        Assert.Equal(true, ours[1].GetTagItem("minos.cache.hit"));
        Assert.Equal(296, ours[1].GetTagItem("gen_ai.usage.input_tokens"));
        var successes = collector.GetSnapshot().Where(record => record.Id.Id == 1001).ToArray();
        Assert.Equal(["False", "True"], successes.Select(record => record.StructuredState!.Single(pair => string.Equals(pair.Key, "Cached", StringComparison.Ordinal)).Value));
    }
}
