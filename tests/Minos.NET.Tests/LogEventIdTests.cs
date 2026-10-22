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

        Assert.Equal([1001, 1002, 1003, 1004, 1005, 1006, 1007, 1008, 1009, 1010, 1011, 1012, 1013], ids.Order());
    }
}
