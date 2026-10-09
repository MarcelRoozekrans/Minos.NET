using Microsoft.Extensions.Logging;

namespace Minos.Shared;

/// <summary>
/// A factory whose one logger is enabled at every level and discards everything, so every log call runs. Compiled into
/// the AOT smoke app and, linked, into the benchmarks.
/// </summary>
internal sealed class DiscardingLoggerFactory : ILoggerFactory
{
    public static readonly DiscardingLoggerFactory Instance = new();

    /// <summary>Gets how many log calls the logger has received, so a caller can prove the logged path ran.</summary>
    public static int Calls => DiscardingLogger.Instance.Calls;

    public ILogger CreateLogger(string categoryName) => DiscardingLogger.Instance;

    public void AddProvider(ILoggerProvider provider)
    {
    }

    public void Dispose()
    {
    }

    private sealed class DiscardingLogger : ILogger
    {
        public static readonly DiscardingLogger Instance = new();

        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Interlocked.Increment(ref _calls);
        }
    }
}
