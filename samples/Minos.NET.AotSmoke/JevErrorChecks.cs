namespace Minos.AotSmoke;

/// <summary><see cref="JevError"/>'s public constructor under Native AOT, as a test double or a caller's own error uses it.</summary>
internal static class JevErrorChecks
{
    [Covers("Minos.JevError.JevError(Minos.JevErrorKind kind, string! message) -> void")]
    public static void HandBuiltErrorCarriesItsKindAndMessage()
    {
        var error = new JevError(JevErrorKind.Timeout, "The call took too long.");

        Program.Check(
            error.Kind == JevErrorKind.Timeout
                && string.Equals(error.Message, "The call took too long.", StringComparison.Ordinal)
                && error.StatusCode is null
                && error.Failures.Count == 0
                && string.Equals(error.ToString(), "Timeout: The call took too long.", StringComparison.Ordinal),
            "new JevError(kind, message) carries its kind and message and nothing else under Native AOT");
        Program.Check(
            SmokeAssert.Throws<ArgumentNullException>(() => _ = new JevError(JevErrorKind.Timeout, null!)),
            "new JevError rejects a null message under Native AOT");
    }
}
