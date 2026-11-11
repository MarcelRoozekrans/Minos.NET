#if !NET11_0_OR_GREATER
namespace Minos.Tool;

/// <summary>
/// The cancellation-token overloads of <see cref="TextWriter"/> that .NET 11 adds, for the net10.0 build of the tool.
/// On net11.0 the instance methods are used and this class is not compiled, so one call site compiles for both. The tool passes
/// <see cref="CancellationToken.None"/>: it reports a cancelled run and lists what that run saved, so those writes must not be cancelled.
/// </summary>
internal static class TextWriterCancellationExtensions
{
    /// <summary>Writes a string followed by a line terminator, unless the token is already cancelled.</summary>
    /// <param name="writer">The writer.</param>
    /// <param name="value">The text to write.</param>
    /// <param name="cancellationToken">Cancels the write before it starts.</param>
    /// <returns>The write.</returns>
    internal static Task WriteLineAsync(this TextWriter writer, string? value, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(writer);
        cancellationToken.ThrowIfCancellationRequested();
        return writer.WriteLineAsync(value);
    }
}
#endif
