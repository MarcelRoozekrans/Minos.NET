using Microsoft.Extensions.Logging;

namespace ZeroAlloc.Jev.Shared;

/// <summary>
/// A factory whose one logger is enabled at every level and discards everything, so every log call runs. Compiled into
/// the AOT smoke app and, linked, into the benchmarks.
/// </summary>
internal sealed class DiscardingLoggerFactory : ILoggerFactory
{
    public static readonly DiscardingLoggerFactory Instance = new();

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

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
        }
    }
}
