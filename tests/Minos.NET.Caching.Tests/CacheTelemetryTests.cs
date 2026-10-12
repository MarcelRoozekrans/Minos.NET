using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;

namespace Minos.Caching.Tests;

public sealed class CacheTelemetryTests
{
    [Fact]
    public async Task A_hit_is_tagged_on_the_span_and_logged_as_cached()
    {
        using var services = Responses.Services();
        var spans = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => string.Equals(source.Name, "Minos", StringComparison.Ordinal),
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = span => { lock (spans) { spans.Add(span); } },
        };
        ActivitySource.AddActivityListener(listener);
        var collector = new FakeLogCollector();
        using var loggerFactory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Debug).AddProvider(new FakeLoggerProvider(collector)));
        using var client = new ScriptedClient().AsBuilder()
            .UseOpenTelemetry()
            .UseLogging(loggerFactory)
            .UseCaching()
            .Build(services);

        // A model no other test uses: test classes run in parallel, and the listener hears every Minos span in the process.
        var request = Responses.Request() with { Model = "cache-telemetry-test" };
        await client.EvaluateAsync(request);
        await client.EvaluateAsync(request);
        spans.RemoveAll(span => !string.Equals(span.GetTagItem("gen_ai.request.model") as string, "cache-telemetry-test", StringComparison.Ordinal));

        Assert.Equal(2, spans.Count);
        Assert.Null(spans[0].GetTagItem("minos.cache.hit"));
        Assert.Equal(true, spans[1].GetTagItem("minos.cache.hit"));
        Assert.Equal(296, spans[1].GetTagItem("gen_ai.usage.input_tokens"));
        var successes = collector.GetSnapshot().Where(record => record.Id.Id == 1001).ToArray();
        Assert.Equal(["False", "True"], successes.Select(record => record.StructuredState!.Single(pair => string.Equals(pair.Key, "Cached", StringComparison.Ordinal)).Value));
    }
}
