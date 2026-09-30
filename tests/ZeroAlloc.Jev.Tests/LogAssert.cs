using Microsoft.Extensions.Logging.Testing;

namespace ZeroAlloc.Jev.Tests;

/// <summary>Reads a <see cref="FakeLogRecord"/>'s structured fields.</summary>
internal static class LogAssert
{
    /// <summary>Gets a structured field's value, <see langword="null"/> when the event logged a null.</summary>
    /// <exception cref="InvalidOperationException">The record has no field named <paramref name="key"/>.</exception>
    public static string? Field(FakeLogRecord record, string key)
    {
        var state = record.StructuredState ?? throw new InvalidOperationException("The record has no structured state.");
        for (var i = 0; i < state.Count; i++)
        {
            if (string.Equals(state[i].Key, key, StringComparison.Ordinal))
            {
                return state[i].Value;
            }
        }

        throw new InvalidOperationException($"The record has no '{key}' field.");
    }
}
