namespace Minos.AotSmoke;

/// <summary><see cref="DecisionError"/>'s public constructor under Native AOT, as a test double or a caller's own error uses it.</summary>
internal static class DecisionErrorChecks
{
    [Covers("Minos.DecisionError.DecisionError(Minos.DecisionErrorKind kind, string! message) -> void")]
    public static void HandBuiltErrorCarriesItsKindAndMessage()
    {
        var error = new DecisionError(DecisionErrorKind.Timeout, "The call took too long.");

        Program.Check(
            error.Kind == DecisionErrorKind.Timeout
                && string.Equals(error.Message, "The call took too long.", StringComparison.Ordinal)
                && error.StatusCode is null
                && error.Failures.Count == 0
                && string.Equals(error.ToString(), "Timeout: The call took too long.", StringComparison.Ordinal),
            "new DecisionError(kind, message) carries its kind and message and nothing else under Native AOT");
        Program.Check(
            SmokeAssert.Throws<ArgumentNullException>(() => _ = new DecisionError(DecisionErrorKind.Timeout, null!)),
            "new DecisionError rejects a null message under Native AOT");
    }
}
