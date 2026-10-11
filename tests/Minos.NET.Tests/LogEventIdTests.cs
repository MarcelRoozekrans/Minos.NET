using System.Reflection;
using Microsoft.Extensions.Logging;
using Minos.SystemOne;

namespace Minos.Tests;

public sealed class LogEventIdTests
{
    [Fact]
    public void Every_log_message_has_a_unique_event_id()
    {
        var ids = new[] { typeof(DecisionLog), typeof(SystemOneLog) }
            .SelectMany(type => type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
            .Select(method => method.GetCustomAttribute<LoggerMessageAttribute>())
            .OfType<LoggerMessageAttribute>()
            .Select(attribute => attribute.EventId)
            .ToList();

        Assert.Equal(6, ids.Count);
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }
}
