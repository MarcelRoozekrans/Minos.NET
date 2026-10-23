using Microsoft.Extensions.Logging;

namespace Minos.Recording.Tests;

/// <summary>A logger provider whose loggers throw on every write, as a broken log sink would.</summary>
internal sealed class ThrowingLoggerProvider : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new ThrowingLogger();

    public void Dispose()
    {
    }

    private sealed class ThrowingLogger : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => throw new InvalidOperationException("The log sink is down.");
    }
}
