namespace Minos.Docs.Tests;

#region Observability_Logging
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Minos;

// One question, so that the events are easy to read.
[JevQuestions]
public partial record ObservedUrgency
{
    [Noul("Does this convey urgency?")]
    public partial Noul IsUrgent { get; }
}

public static class ObservedLogging
{
    // A client logs through the ILoggerFactory it is given. FakeLoggerProvider keeps every record in memory, which
    // is what a test wants. An application would add its own provider instead, such as the console or Serilog.
    public static async Task<IReadOnlyList<FakeLogRecord>> EvaluateAndCollectAsync(
        HttpClient http, JevClientOptions options, CancellationToken cancellationToken)
    {
        using var provider = new FakeLoggerProvider();
        using var loggers = LoggerFactory.Create(builder => builder
            .AddProvider(provider)
            .SetMinimumLevel(LogLevel.Debug));
        using var jev = new JevClient(http, options, loggers);

        // The result is not inspected here: each outcome, a success or a failure, is one log event.
        await jev.EvaluateAsync<ObservedUrgency>("Help! The server is down.", cancellationToken);

        return provider.Collector.GetSnapshot();
    }
}
#endregion
