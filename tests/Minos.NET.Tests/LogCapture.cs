using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;

namespace Minos.Tests;

/// <summary>
/// A real <see cref="LoggerFactory"/> over Microsoft's <see cref="FakeLoggerProvider"/>, for tests that construct
/// <see cref="JevClient"/> through its public constructors. Uses no internal type, so the integration tests link it.
/// </summary>
internal sealed class LogCapture : IDisposable
{
    private readonly FakeLoggerProvider _provider = new();
    private readonly LoggerFactory _factory;

    public LogCapture()
        : this(LogLevel.Trace)
    {
    }

    public LogCapture(LogLevel minimumLevel)
        => _factory = new LoggerFactory([_provider], new LoggerFilterOptions { MinLevel = minimumLevel });

    public ILoggerFactory Factory => _factory;

    public IReadOnlyList<FakeLogRecord> Records => _provider.Collector.GetSnapshot();

    public int[] EventIds => [.. Records.Select(record => record.Id.Id)];

    public FakeLogRecord Only(int eventId)
    {
        var matches = Records.Where(record => record.Id.Id == eventId).ToArray();

        // HLQ005 fires on the method name alone: this is xUnit's Assert.Single(IEnumerable), not System.Linq.Enumerable.Single().
#pragma warning disable HLQ005
        return Assert.Single(matches);
#pragma warning restore HLQ005
    }

    public void Dispose()
    {
        _factory.Dispose();
        _provider.Dispose();
    }
}
