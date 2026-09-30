using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace ZeroAlloc.Jev.AotSmoke;

/// <summary>An in-process provider that keeps every record, so the smoke app can check the client's events under Native AOT.</summary>
internal sealed class CapturingLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentQueue<CapturedLog> _records = new();

    /// <summary>Gets the records so far, in the order they were logged.</summary>
    public CapturedLog[] Records => _records.ToArray();

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, _records);

    public void Dispose()
    {
    }

    private sealed class CapturingLogger(string category, ConcurrentQueue<CapturedLog> records) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var fields = new Dictionary<string, object?>(StringComparer.Ordinal);
            if (state is IReadOnlyList<KeyValuePair<string, object?>> values)
            {
                for (var i = 0; i < values.Count; i++)
                {
                    fields[values[i].Key] = values[i].Value;
                }
            }

            records.Enqueue(new CapturedLog(category, logLevel, eventId.Id, formatter(state, exception), fields));
        }
    }
}

/// <summary>One captured record: its category, level, event id, formatted message and structured fields.</summary>
internal sealed record CapturedLog(string Category, LogLevel Level, int EventId, string Message, IReadOnlyDictionary<string, object?> Fields)
{
    /// <summary>Gets a field's value, or <see langword="null"/> when the record has no such field.</summary>
    public object? Field(string key) => Fields.TryGetValue(key, out var value) ? value : null;
}
